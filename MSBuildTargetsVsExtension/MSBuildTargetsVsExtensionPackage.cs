using EnvDTE80;
using Microsoft.Build.Execution;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Framework;
using System.ComponentModel.Design;
using EnvDTE;
using System.IO;
using System.Windows.Input;
using System.Linq;
using System.Threading.Tasks;

namespace MSBuildTargetsVsExtension
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration("#110", "#112", "1.0", IconResourceID = 400)] // This attribute is used to register the information needed to show this package in the Help/About dialog of Visual Studio.
    [ProvideMenuResource("Menus.ctmenu", 1)] // This attribute is needed to let the shell know that this package exposes some menus.
    [Guid(Constants.GuidMsBuildTargetsVsExtensionPkgString)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    public sealed class MSBuildTargetsVsExtensionPackage : AsyncPackage
    {
        public bool ShowMessages { get; set; }

        private DTE2 _dte;

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
        /// <param name="cancellationToken">A cancellation token to monitor for initialization cancellation, which can occur when VS is shutting down.</param>
        /// <param name="progress">A provider for progress updates.</param>
        /// <returns>A task representing the async work of package initialization, or an already completed task if there is none. Do not return null from this method.</returns>
        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            // When initialized asynchronously, the current thread may be a background thread at this point.
            // Do any initialization that requires the UI thread after switching to the UI thread.
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            _dte = await GetServiceAsync(typeof(DTE)) as DTE2;
            if (_dte == null)
            {
                VsShellUtilities.ShowMessageBox(
                    this,
                    "Extension failed to load.",
                    Constants.ProductName,
                    OLEMSGICON.OLEMSGICON_CRITICAL,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);

                return;
            }

            var commandService = await GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (commandService == null)
            {
                VsShellUtilities.ShowMessageBox(
                    this,
                    "Extension failed to load.",
                    Constants.ProductName,
                    OLEMSGICON.OLEMSGICON_CRITICAL,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);

                return;
            }

            // Add menu command handlers (commands must exist in the .vsct file)
            // NOTE: AddCommand() must be called from the UI thread

            var menuCommandIdStart = new CommandID(Constants.GuidMsBuildTargetsVsExtensionCmdSet, (int)PkgCmdID.CmdStart);
            var menuItemStart = new MenuCommand(MenuItemCallback, menuCommandIdStart);
            commandService.AddCommand(menuItemStart);

            var menuCommandIdStartDebugging = new CommandID(Constants.GuidMsBuildTargetsVsExtensionCmdSet, (int)PkgCmdID.CmdStartDebugging);
            var menuItemStartDebugging = new MenuCommand(MenuItemCallback, menuCommandIdStartDebugging);
            commandService.AddCommand(menuItemStartDebugging);

            var menuCommandIdSelectTarget = new CommandID(Constants.GuidMsBuildTargetsVsExtensionCmdSet, (int)PkgCmdID.CmdSelectTarget);
            var menuItemSelectTarget = new MenuCommand(ShowToolWindow, menuCommandIdSelectTarget);
            commandService.AddCommand(menuItemSelectTarget);
        }

        private void ShowToolWindow(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var targetNamesCount = new SortedDictionary<string, int>();
            var selectedProjects = new List<EnvDTE.Project>();

            //Ignore standard targets as we dont want to display a huge target list
            var winDrive = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var ignoreFolders = new[] {
                Path.Combine(winDir, "Microsoft.NET"),
                Path.Combine(winDrive, "Program Files (x86)"),
                Path.Combine(winDrive, "Program Files")
            };

            var projCount = _dte.SelectedItems.Count;
            for (var k = 1; k <= projCount; k++) //Iterate through all selected items
            {
                var selectedProject = _dte.SelectedItems.Item(k).Project;
                selectedProjects.Add(selectedProject); //Add to selectedProjects

                var evalProject = ProjectCollection.GlobalProjectCollection.LoadProject(selectedProject.FullName);
                var execProject = evalProject.CreateProjectInstance();

                foreach (var target in execProject.Targets)
                {
                    var targetName = $"{target.Value.Name}";
                    var targetFilePath = Path.GetDirectoryName(target.Value.FullPath);

                    if (ignoreFolders.Any(t => Helpers.IsPathSubpathOf(t, targetFilePath)))
                        continue; // Ignore

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

                ExecuteTarget(selectedProjects, targetName);
            }
        }

        private void ExecuteTarget(IEnumerable<EnvDTE.Project> selectedProjects, string targetName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var buildInfo = new List<ProjectBackgroundBuildInfo>();
            foreach (var project in selectedProjects)
            {
                project.Save(); //Save the EnvDTE project instance

                // Add to ProjectBackgroundBuildInfo list
                var config = project.ConfigurationManager.ActiveConfiguration;
                buildInfo.Add(new ProjectBackgroundBuildInfo { 
                    ProjectName = project.Name, 
                    ConfigName = config.ConfigurationName, 
                    PlatformName = config.PlatformName, 
                    BuildParameters = CreateBuildParameters(), 
                    BuildRequestData = CreateBuildRequestData(project, targetName)
                });
            }

            var output = new OutputWindowLoggerAdaptor(true);
            output.Activate();

            // Run build in background thread
            var previousCursor = Mouse.OverrideCursor;
            Mouse.OverrideCursor = Cursors.Wait;
            Task.Run(() =>
            {                
                var succeeded = 0;
                var failed = 0;

                using (var buildManager = new BuildManager())
                {
                    foreach (var bi in buildInfo)
                    {
                        var res = buildManager.Build(bi.BuildParameters, bi.BuildRequestData);

                        if (res.OverallResult == BuildResultCode.Failure)
                        {
                            output.OutputString($"========== '{bi.ProjectName} -> {targetName}' FAILED ==========");
                            failed++;
                        }
                        else
                        {
                            output.OutputString($"========== '{bi.ProjectName} -> {targetName}' succeeded ==========");
                            succeeded++;
                        }

                        if (failed == 0)
                            output.OutputString($"========== {targetName}: {succeeded} project(s) succeeded ==========");
                        else
                            output.OutputString($"========== {targetName}: {succeeded} project(s) succeeded, {failed} project(s) FAILED ==========");
                    }                    
                }
                
            }).ContinueWith(t =>
            {
                Mouse.OverrideCursor = previousCursor;
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void MenuItemCallback(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var menuCommand = (MenuCommand)sender;
            var debugging = menuCommand.CommandID.ID == PkgCmdID.CmdStartDebugging;
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
            ThreadHelper.ThrowIfNotOnUIThread();

            var globalProperties = new Dictionary<string, string>();

            var config = proj.ConfigurationManager.ActiveConfiguration;
            globalProperties["Configuration"] = config.ConfigurationName;
            globalProperties["Platform"] = config.PlatformName.Replace(" ", "");

            var solutionDir = Path.GetDirectoryName(_dte.Solution.FullName);
            globalProperties["SolutionDir"] = solutionDir;

            var buildRequest = new BuildRequestData(proj.FullName, globalProperties, null, new[] { target }, null, BuildRequestDataFlags.ReplaceExistingProjectInstance);
            return buildRequest;
        }

        class ProjectBackgroundBuildInfo
        {
            public string ProjectName { get; set; }
            public string ConfigName { get; set; }
            public string PlatformName { get; set; }
            public BuildParameters BuildParameters { get; set; }
            public BuildRequestData BuildRequestData { get; set; }
        }
    }
}
