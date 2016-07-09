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

namespace MSBuildTargetsVsExtension
{
    /// <summary>
    /// This is the class that implements the package exposed by this assembly.
    ///
    /// The minimum requirement for a class to be considered a valid package for Visual Studio
    /// is to implement the IVsPackage interface and register itself with the shell.
    /// This package uses the helper classes defined inside the Managed Package Framework (MPF)
    /// to do it: it derives from the Package class that provides the implementation of the 
    /// IVsPackage interface and uses the registration attributes defined in the framework to 
    /// register itself and its components with the shell.
    /// </summary>   
    [PackageRegistration(UseManagedResourcesOnly = true)] // This attribute tells the PkgDef creation utility (CreatePkgDef.exe) that this class is a package. 
    [InstalledProductRegistration("#110", "#112", "1.0", IconResourceID = 400)] // This attribute is used to register the information needed to show this package in the Help/About dialog of Visual Studio.
    [ProvideMenuResource("Menus.ctmenu", 1)] // This attribute is needed to let the shell know that this package exposes some menus.
    [Guid(GuidList.GuidMsBuildTargetsVsExtensionPkgString)]
    public sealed class MSBuildTargetsVsExtensionPackage : Package
    {
        public bool ShowMessages { get; set; }
        private VsOutputWindowLogger _vsOutputWindowLogger;
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
            
            _vsOutputWindowLogger = new VsOutputWindowLogger(this);
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
                var projects = new List<Project>();
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

            if (debugging)
                _dte.ExecuteCommand("Debug.Start");
            else
                _dte.ExecuteCommand("Debug.StartWithoutDebugging");
           
        }

        private void ShowToolWindow(object sender, EventArgs e)
        {
            var targetNamesCount = new SortedDictionary<string, int>();
            var selectedProjects = new List<Project>();

            var projCount = _dte.SelectedItems.Count;            
            for (var k = 1; k <= projCount; k++) //Iterate through all selected items
            {
                var selectedProject = _dte.SelectedItems.Item(k).Project;
                selectedProjects.Add(selectedProject); //Add to selectedProjects

                var evalProject = Microsoft.Build.Evaluation.ProjectCollection.GlobalProjectCollection.LoadProject(selectedProject.FullName);
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

                    var targetName = string.Format("{0}", target.Value.Name);

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

                Cursor previousCursor = Mouse.OverrideCursor;
                Mouse.OverrideCursor = Cursors.Wait;
                System.Threading.Tasks.Task.Run(() => 
                {                    
                    ExecuteMsBuildTarget(targetName, selectedProjects);
                })
                .ContinueWith((t) => 
                {
                    Mouse.OverrideCursor = previousCursor;
                }, System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());
            }
        }

        private void ExecuteMsBuildTarget(string targetName, IEnumerable<Project> projects)
        {            
            Application.Current.Dispatcher.Invoke(new Action(() =>
            {
                _vsOutputWindowLogger.OutputString(string.Format("------ Executing: '{0}' for selected project(s) ------", targetName));
                _dte.StatusBar.Text = "Executing target...";
            }));
            
            var succeeded = 0;
            var failed = 0;
            foreach(var project in projects)
            {
                project.Save(); //Save the project in Visual Studio

                var evalProject = Microsoft.Build.Evaluation.ProjectCollection.GlobalProjectCollection.LoadProject(project.FullName);
                var execProject = evalProject.CreateProjectInstance();

                var config = project.ConfigurationManager.ActiveConfiguration;
                execProject.SetProperty("Configuration", config.ConfigurationName);
                execProject.SetProperty("Platform", config.PlatformName);

                var solutionDir = Path.GetDirectoryName(_dte.Solution.FullName);
                execProject.SetProperty("SolutionDir", solutionDir);

                var msg = string.Format("------ Executing: '{0} -> {1}' for configuration '{2}' and platform '{3}' ------", project.Name, targetName, config.ConfigurationName, config.PlatformName);
                Application.Current.Dispatcher.Invoke(new Action(() => _vsOutputWindowLogger.OutputString(msg)));

                var buildResult = execProject.Build(targetName, new[] { _vsOutputWindowLogger });
                if (buildResult)
                {
                    msg = string.Format("========== '{0} -> {1}' succeeded ==========", project.Name, targetName);
                    succeeded++;
                }
                else
                {
                    msg = string.Format("========== '{0} -> {1}' failed ==========", project.Name, targetName);
                    failed++;
                }
                Application.Current.Dispatcher.Invoke(new Action(() => _vsOutputWindowLogger.OutputString(msg) ));
            }

            Application.Current.Dispatcher.Invoke(new Action(() =>
            {
                _dte.StatusBar.Text = "Target finished";
                _vsOutputWindowLogger.OutputString(string.Format("========== {0}: {1} project(s) succeeded, {2} project(s) failed ==========", targetName, succeeded, failed));
            }));       
        }
    }
}
