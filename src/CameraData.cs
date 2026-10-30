using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TransportFever3Trainer
{
    static class CameraData
    {
        static bool Finite(object value, string key)
        { double n = Data.Num(value, key, double.NaN); return !double.IsNaN(n) && !double.IsInfinity(n); }
        public static bool HasCenter(object value) { return Finite(value, "x") && Finite(value, "y"); }
        public static bool HasPose(object value)
        { return HasCenter(value) && new[] { "z", "w", "v" }.All(k => Finite(value, k)); }
        public static void MergeSnapshot(Dictionary<string, object> state, string previousScope, object previousCamera,
            double previousCameraAt, string previousCameraError, bool previousCameraFresh)
        {
            double incomingAt = Data.Num(state, "cameraAt", double.NaN);
            if (previousScope != Data.Str(state, "nameScope") || !HasCenter(previousCamera)) return;
            if (!(double.IsNaN(incomingAt) || (!double.IsNaN(previousCameraAt) && previousCameraAt > incomingAt))) return;
            bool incomingFailed = Data.Str(state, "cameraError").Length > 0;
            state["camera"] = previousCamera;
            state["cameraAt"] = double.IsNaN(previousCameraAt) ? null : (object)previousCameraAt;
            state["cameraFresh"] = incomingFailed ? false : previousCameraFresh;
            if (!incomingFailed) state["cameraError"] = previousCameraError;
        }
        public static Dictionary<string, object> ReadReply(object value, string request, string world, string scope, bool stocks)
        {
            var reply = Data.Obj(value);
            if (!Data.Bool(reply, "ok") || Data.Str(reply, "request") != request || Data.Str(reply, "world") != world || Data.Str(reply, "nameScope") != scope)
                throw new InvalidDataException("镜头回复与当前请求或地图不一致，请重新读取。");
            if (!HasCenter(Data.Get(reply, "camera"))) throw new InvalidDataException("游戏未返回有效的镜头中心坐标。");
            if (stocks && !(Data.Get(reply, "stocks") is object[])) throw new InvalidDataException("游戏未返回当前库存位置。");
            return reply;
        }
    }
}
