﻿using System;
using System.Collections.Generic;

namespace DGP.Genshin.Helpers
{
    /// <summary>
    /// 在分析与崩溃中使用
    /// </summary>
    internal class Info
    {
        private Dictionary<string, string> analyticsInfo = new();
        public Info(string key,string value)
        {
            analyticsInfo[key] = value;
        }

        public Info(Type pageType, bool result)
        {
            analyticsInfo[pageType.ToString()] = result.ToString();
        }

        public IDictionary<string, string> Build()
        {
            return analyticsInfo;
        }
    }
}