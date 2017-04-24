using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using EnvDTE;
using EnvDTE80;
using Microsoft.Build.Execution;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System.IO;
using System.Windows.Input;
using System.Windows;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Framework;

namespace MSBuildTargetsVsExtension
{
    [PackageRegistration(UseManagedResourcesOnly = true)] // This attribute tells the PkgDef creation utility (CreatePkgDef.exe) that this class is a package. 
    [InstalledProductRegistration("#110", "#112", "1.0", IconResourceID = 400)] // This attribute is used to register the information needed to show this package in the Help/About dialog of Visual Studio.
    [ProvideMenuResource("Menus.ctmenu", 1)] // This attribute is needed to let the shell know that this package exposes some menus.
    [Guid(GuidList.GuidMsBuildTargetsVsExtensionPkgString)]
    public sealed class MSBuildTargetsVsExtensionPackage : Package
    {
        public bool ShowMessages { get; set; }

        private DTE2 _dte;
        private IVsUIShell _vsUiShell;

        /// <summary>
        /// Default constructor of the package.
        /// Inside this method you can place any initialization code that does not require 
        /// any Visual Studio service because at this point the package object is created but 
        /// not sited yet inside Visual Studio environment. The place to do all the other 
        /// initialization is the Initialize method.
        /// </summary>
        public MSBuildTargetsVsExtensionPackage()
        {

        }

        /// <summary>
        /// Initialization of the package; this method is called right after the package is sited, so this is the place
        /// where you can put all the initialization code that rely on services provided by VisualStudio.
        /// </summary>
        protected override void Initialize()
        {
            base.Initialize();
            
            _dte = (DTE2)GetService(typeof(DTE));
            _vsUiShell = (IVsUIShell)GetService(typeof(SVsUIShell));

            // Add our command handlers for menu (commands must exist in the .vsct file)
            var mcs = GetService(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (null != mcs)
            {
                var menuCommandID = new CommandID(GuidList.GuidMsBuildTargetsVsExtensionCmdSet, (int)PkgCmdIDList.CmdStart);
                var menuItem = new MenuCommand(MenuItemCallback, menuCommandID);
                mcs.AddCommand(menuItem);

                menuCommandID = new CommandID(GuidList.GuidMsBuildTargetsVsExtensionCmdSet, (int)PkgCmdIDList.CmdStartDebugging);
                menuItem = new MenuCommand(MenuItemCallback, menuCommandID);
                mcs.AddCommand(menuItem);

                menuCommandID  = new CommandID(GuidList.GuidMsBuildTargetsVsExtensionCmdSet, (int)PkgCmdIDList.CmdSelectTarget);
                menuItem = new MenuCommand(ShowToolWindow, menuCommandID);
                mcs.AddCommand(menuItem); 
            }
        }

        private void MenuItemCallback(object sender, EventArgs e)
        {
            var menuCommand = (MenuCommand)sender;
            var debugging = menuCommand.CommandID.ID == PkgCmdIDList.CmdStartDebugging;
            var selectedItemCount = _dte.SelectedItems.Count;

            if (selectedItemCount > 1) //Multiple projects selected?
            {
                var projects = new List<EnvDTE.Project>();
                for (int k = 0; k < selectedItemCount; k++)
                {
                    var selectedProject = _dte.SelectedItems.Item(k + 1).Project;
                    projects.Add(selectedProject);
                }

                var sb = _dte.Solution.SolutionBuild;
                var newStartups = projects.Select(t => t.FullName).ToArray<object>();
                sb.StartupProjects = newStartups;
            }
            else //Single project selected
            {
                var startupProjectProperty = _dte.Solution.Properties.Item("StartupProject");
                startupProjectProperty.Value = _dte.SelectedItems.Item(1).Project.Name;
            }

            _dte.ExecuteCommand(debugging ? "Debug.Start" : "Debug.StartWithoutDebugging");
        }

        private void ShowToolWindow(object sender, EventArgs e)
        {
            var targetNamesCount = new SortedDictionary<string, int>();
            var selectedProjects = new List<EnvDTE.Project>();

            var projCount = _dte.SelectedItems.Count;            
            for (var k = 1; k <= projCount; k++) //Iterate through all selected items
            {
                var selectedProject = _dte.SelectedItems.Item(k).Project;
                selectedProjects.Add(selectedProject); //Add to selectedProjects

                var evalProject = ProjectCollection.GlobalProjectCollection.LoadProject(selectedProject.FullName);
                var execProject = evalProject.CreateProjectInstance();

                foreach (var target in execProject.Targets)
                {
                    //Ignore standard targets as we dont want to display a huge target list
                    var ignoreFolders = new[] {
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET"),
                        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
                    };                    
                    var targetFilePath = Path.GetDirectoryName(target.Value.FullPath);

                    if (ignoreFolders.Any(t => Helpers.IsPathSubpathOf(t, targetFilePath)))
                        continue;

                    var targetName = $"{target.Value.Name}";

                    if (targetNamesCount.ContainsKey(targetName))
                        targetNamesCount[targetName]++;
                    else
                        targetNamesCount[targetName] = 1;
                }
            }

            var commonTargets = targetNamesCount.Where(t => t.Value == projCount).Select(t => t.Key);

            var selectTargetsWindow = new SelectTargetsWindow { DataContext = this };
            foreach (var item in commonTargets)
                selectTargetsWindow.ItemsComboBox.Items.Add(item);
            selectTargetsWindow.ItemsComboBox.SelectedIndex = 0;

            if (selectTargetsWindow.ShowDialog() == true)
            {
                var targetName = (string)selectTargetsWindow.ItemsComboBox.SelectedItem;

                var previousCursor = Mouse.OverrideCursor;
                Mouse.OverrideCursor = Cursors.Wait;
                System.Threading.Tasks.Task.Run(() =>
                {
                    ExecuteTarget(selectedProjects, targetName);
                }).ContinueWith(t =>
                {
                    Mouse.OverrideCursor = previousCursor;
                }, System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());
            }
        }

        private void ExecuteTarget(IEnumerable<EnvDTE.Project> selectedProjects, string targetName)
        {
            var output = new OutputWindowLoggerAdaptor(true);
            var succeeded = 0;
            var failed = 0;
            foreach (var project in selectedProjects)
            {
                project.Save(); //Save the project in Visual Studio

                var config = project.ConfigurationManager.ActiveConfiguration;
                output.OutputString($"------ Executing: '{project.Name} -> {targetName}' for '{config.ConfigurationName}|{config.PlatformName}' ------");

                using (var buildManager = new BuildManager())
                {
                    var res = buildManager.Build(CreateBuildParameters(), CreateBuildRequestData(project, targetName));
                    if (res.OverallResult == BuildResultCode.Failure)
                    {
                        output.OutputString($"========== '{project.Name} -> {targetName}' FAILED ==========");
                        failed++;
                    }
                    else
                    {
                        output.OutputString($"========== '{project.Name} -> {targetName}' succeeded ==========");
                        succeeded++;
                    }
                }

                if (failed == 0)
                    output.OutputString($"========== {targetName}: {succeeded} project(s) succeeded ==========");
                else
                    output.OutputString($"========== {targetName}: {succeeded} project(s) succeeded, {failed} project(s) FAILED ==========");
            }
        }

        private BuildParameters CreateBuildParameters()
        {
            var projectCollection = new ProjectCollection();
            var buildParameters = new BuildParameters(projectCollection)
            {
                Loggers = new List<ILogger>() { new OutputWindowLoggerAdaptor(ShowMessages) }
            };

            return buildParameters;
        }

        private BuildRequestData CreateBuildRequestData(EnvDTE.Project proj, string target)
        {
            var globalProperties = new Dictionary<string, string>();

            var config = proj.ConfigurationManager.ActiveConfiguration;
            globalProperties["Configuration"] = config.ConfigurationName;
            globalProperties["Platform"] = config.PlatformName.Replace(" ", "");

            var solutionDir = Path.GetDirectoryName(_dte.Solution.FullName);
            globalProperties["SolutionDir"] = solutionDir;

            var buildRequest = new BuildRequestData(proj.FullName, globalProperties, null, new[] { target }, null, BuildRequestDataFlags.ReplaceExistingProjectInstance);
            return buildRequest;
        }

    }
}
