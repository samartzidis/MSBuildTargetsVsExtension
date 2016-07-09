// PkgCmdID.cs
// MUST match PkgCmdID.h

namespace MSBuildTargetsVsExtension
{
    static class PkgCmdIDList
    {
        public const uint CmdStart = 0x100;
        public const uint CmdStartDebugging = 0x101;
        public const uint CmdSelectTarget = 0x102;
    };
}