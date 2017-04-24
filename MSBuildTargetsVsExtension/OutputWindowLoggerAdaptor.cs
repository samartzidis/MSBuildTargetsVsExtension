using System;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace MSBuildTargetsVsExtension
{
    internal class OutputWindowLoggerAdaptor : Microsoft.Build.Framework.ILogger
    {
        private readonly IVsOutputWindowPane _pane;
        private readonly bool _showMessages;

        public OutputWindowLoggerAdaptor(bool showMessages)
        {
            _showMessages = showMessages;

            var outWindow = Package.GetGlobalService(typeof(SVsOutputWindow)) as IVsOutputWindow;
            var generalPaneGuid = VSConstants.GUID_BuildOutputWindowPane;
            outWindow.GetPane(ref generalPaneGuid, out _pane);
        }

        public void Initialize(Microsoft.Build.Framework.IEventSource eventSource)
        {
            eventSource.ErrorRaised += eventSource_ErrorRaised;
            eventSource.MessageRaised += eventSource_MessageRaised;
            eventSource.WarningRaised += eventSource_WarningRaised;
        }

        private void eventSource_WarningRaised(object sender, Microsoft.Build.Framework.BuildWarningEventArgs e)
        {
            OutputString(EventArgsFormatter.FormatEventMessage(e, false, true));
        }

        private void eventSource_MessageRaised(object sender, Microsoft.Build.Framework.BuildMessageEventArgs e)
        {
            if (!_showMessages || e.Importance < Microsoft.Build.Framework.MessageImportance.High)
                return;

            OutputString(EventArgsFormatter.FormatEventMessage(e, false, true));
        }

        private void eventSource_ErrorRaised(object sender, Microsoft.Build.Framework.BuildErrorEventArgs e)
        {
            OutputString(EventArgsFormatter.FormatEventMessage(e, false, true));            
        }

        public void OutputString(string msg)
        {
            _pane.Activate();
            _pane.OutputString(msg + Environment.NewLine);            
        }

        public string Parameters
        {
            get;
            set;
        }

        public void Shutdown()
        {

        }

        public Microsoft.Build.Framework.LoggerVerbosity Verbosity
        {
            get;
            set;
        }
    }
}
