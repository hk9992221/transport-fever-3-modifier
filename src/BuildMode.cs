using System;

namespace TransportFever3Trainer
{
    static class BuildMode
    {
        public static bool Offline
        {
            get { return Environment.GetEnvironmentVariable("TF3_MODIFIER_BUILD_CHECK") == "1" || Array.IndexOf(Environment.GetCommandLineArgs(), "--layout-check") >= 0; }
        }
        public static void RequireLiveAccess()
        { if (Offline) throw new InvalidOperationException("离线构建检查禁止连接游戏或加载运行模块。"); }
    }
}
