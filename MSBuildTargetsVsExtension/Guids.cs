using System;

namespace MSBuildTargetsVsExtension
{
    static class GuidList
    {
        public const string GuidMsBuildTargetsVsExtensionPkgString = "137da963-074e-4dcf-a87a-34857204d497";
        public const string GuidMsBuildTargetsVsExtensionCmdSetString = "9fc10e11-28c8-45b9-abac-8aa4ec3a4346";
        public static readonly Guid GuidMsBuildTargetsVsExtensionCmdSet = new Guid(GuidMsBuildTargetsVsExtensionCmdSetString);
    };
}