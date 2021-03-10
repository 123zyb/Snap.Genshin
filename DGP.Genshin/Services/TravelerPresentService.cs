﻿using DGP.Genshin.Data;
using DGP.Genshin.Data.Character;
using DGP.Snap.Framework.Core.LifeCycle;
using System;

namespace DGP.Genshin.Services
{
    public class TravelerPresentService : IAutoLifeCycle
    {
        public void SetPresentTraveler()
        {
            Element element =
                SettingService.Instance.GetOrDefault(Setting.PresentTravelerElementType,
                                                     Element.Anemo,
                                                     n => (Element)Enum.Parse(typeof(Element), n.ToString()));
            Character TravelerSource = CharacterManager.Instance["Traveler" + element];

            CharacterManager.Instance["TravelerPresent"].ImageUri = TravelerSource.ImageUri;
            CharacterManager.Instance["TravelerPresent"].Element = TravelerSource.Element;
            CharacterManager.Instance["TravelerPresent"].AscensionBoss = TravelerSource.AscensionBoss;
            CharacterManager.Instance["TravelerPresent"].AscensionGemstone = TravelerSource.AscensionGemstone;
            CharacterManager.Instance["TravelerPresent"].AscensionLocal = TravelerSource.AscensionLocal;
            CharacterManager.Instance["TravelerPresent"].AscensionMonster = TravelerSource.AscensionMonster;
            CharacterManager.Instance["TravelerPresent"].TalentDaily = TravelerSource.TalentDaily;
            CharacterManager.Instance["TravelerPresent"].TalentWeekly = TravelerSource.TalentWeekly;
        }

        public void Initialize()
        {
            SetPresentTraveler();
        }

        public void UnInitialize()
        {

        }

        #region 单例
        private static TravelerPresentService instance;
        private static readonly object _lock = new object();
        private TravelerPresentService()
        {

        }
        public static TravelerPresentService Instance
        {
            get
            {
                if (instance == null)
                {
                    lock (_lock)
                    {
                        if (instance == null)
                        {
                            instance = new TravelerPresentService();
                        }
                    }
                }
                return instance;
            }
        }

        public IAutoLifeCycle Self => ((IAutoLifeCycle)Instance).Self;
        #endregion
    }
}
