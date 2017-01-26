﻿using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace KerbalHealth
{
    public class KerbalHealthStatus
    {
        public enum HealthCondition { OK, Exhausted }  // conditions

        string name;
        double hp;
        double lastChange;  // Cached HP change per day (for unloaded vessels)
        double lastMarginalPositiveChange, lastMarginalNegativeChange;  // Cached marginal HP change (in %)
        HealthCondition condition = HealthCondition.OK;
        string trait = null;
        bool onEva = false;  // True if kerbal is on EVA

        public string Name
        {
            get { return name; }
            set
            {
                name = value;
                pcmCached = null;
            }
        }

        public double HP
        {
            get { return hp; }
            set
            {
                if (value < Core.MinHP) hp = Core.MinHP;
                else if (value > MaxHP) hp = MaxHP;
                else hp = value;
            }
        }

        public double Health { get { return (HP - Core.MinHP) / (MaxHP - Core.MinHP); } }  // % of health relative to MaxHealth

        public double LastChange
        {
            get { return lastChange; }
            set { lastChange = value; }
        }

        public double LastMarginalPositiveChange
        {
            get { return lastMarginalPositiveChange; }
            set { lastMarginalPositiveChange = value; }
        }

        public double LastMarginalNegativeChange
        {
            get { return lastMarginalNegativeChange; }
            set { lastMarginalNegativeChange = value; }
        }

        public HealthCondition Condition
        {
            get { return condition; }
            set
            {
                if (value == condition) return;
                switch (value)
                {
                    case HealthCondition.OK:
                        Core.Log("Reviving " + Name + " as " + Trait + "...");
                        PCM.type = ProtoCrewMember.KerbalType.Crew;
                        PCM.trait = Trait;
                        break;
                    case HealthCondition.Exhausted:
                        Core.Log(Name + " (" + Trait + ") is exhausted.");
                        Trait = PCM.trait;
                        PCM.type = ProtoCrewMember.KerbalType.Tourist;
                        break;
                }
                condition = value;
            }
        }

        string Trait
        {
            get { return trait ?? PCM.trait; }
            set { trait = value; }
        }

        public bool IsOnEVA
        {
            get { return onEva; }
            set { onEva = value; }
        }

        ProtoCrewMember pcmCached;
        public ProtoCrewMember PCM
        {
            get
            {
                //if (pcmCached != null) return pcmCached;
                foreach (ProtoCrewMember pcm in HighLogic.fetch.currentGame.CrewRoster.Crew)
                    if (pcm.name == Name)
                    {
                        pcmCached = pcm;
                        return pcm;
                    }
                foreach (ProtoCrewMember pcm in HighLogic.fetch.currentGame.CrewRoster.Tourist)
                    if (pcm.name == Name)
                    {
                        pcmCached = pcm;
                        return pcm;
                    }
                return null;
            }
            set
            {
                Name = value.name;
                pcmCached = value;
            }
        }

        public static double GetMaxHP(ProtoCrewMember pcm)
        {
            return Core.BaseMaxHP + Core.HPPerLevel * pcm.experienceLevel;
        }

        public double MaxHP
        {
            get { return GetMaxHP(PCM); }
        }

        public double TimeToValue(double target)
        {
            double change = HealthChangePerDay(PCM);
            if (change == 0) return double.NaN;
            double res = (target - HP) / change;
            if (res < 0) return double.NaN;
            return res * 21600;
        }

        public double NextConditionHP()
        {
            if (HealthChangePerDay(PCM) > 0)
            {
                switch (Condition)
                {
                    case HealthCondition.OK:
                        return MaxHP;
                    case HealthCondition.Exhausted:
                        return Core.ExhaustionEndHealth * MaxHP;
                }
            }
            switch (Condition)
            {
                case HealthCondition.OK:
                    return Core.ExhaustionStartHealth * MaxHP;
                case HealthCondition.Exhausted:
                    return Core.DeathHealth * MaxHP;
            }
            return double.NaN;
        }

        public double TimeToNextCondition()
        {
            return TimeToValue(NextConditionHP());
        }

        // Returns HP level when marginal HP change balances out "fixed" change. If <= 0, no such level
        public double GetBalanceHP()
        {
            if (LastMarginalPositiveChange <= LastMarginalNegativeChange) return 0;
            return (MaxHP * LastMarginalPositiveChange + LastChange * 100) / (LastMarginalPositiveChange - LastMarginalNegativeChange);
        }

        static int GetCrewCount(ProtoCrewMember pcm)
        {
            return Core.IsInEditor ? ShipConstruction.ShipManifest.CrewCount : (pcm?.seat?.vessel.GetCrewCount() ?? 1);
        }

        static int GetCrewCapacity(ProtoCrewMember pcm)
        {
            return Core.IsInEditor ? ShipConstruction.ShipManifest.GetAllCrew(true).Count : (pcm?.seat?.vessel.GetCrewCapacity() ?? 1);
        }

        static bool isKerbalLoaded(ProtoCrewMember pcm)
        { return pcm?.seat?.vessel != null; }

        double MarginalChange
        {
            get
            {
                return (MaxHP - HP) * (LastMarginalPositiveChange / 100) - (HP - Core.MinHP) * (LastMarginalNegativeChange / 100);
            }
        }

        public static double HealthChangePerDay(ProtoCrewMember pcm)
        {
            double change = 0;
            if (pcm == null) return 0;
            KerbalHealthStatus khs = Core.KerbalHealthList.Find(pcm);
            if (khs == null)
            {
                Core.Log("Error: " + pcm.name + " not found in KerbalHealthList during update!", Core.LogLevel.Error);
                return 0;
            }
            if ((pcm.rosterStatus == ProtoCrewMember.RosterStatus.Assigned && isKerbalLoaded(pcm)) || Core.IsInEditor || khs.IsOnEVA)
            {
                if (isKerbalLoaded(pcm)) khs.IsOnEVA = false;
                khs.LastMarginalPositiveChange = khs.LastMarginalNegativeChange = 0;
                change += Core.AssignedFactor;
                change += Core.LivingSpaceBaseFactor * GetCrewCount(pcm) / GetCrewCapacity(pcm);
                if (!khs.IsOnEVA)
                {
                    if ((GetCrewCount(pcm) > 1) || pcm.isBadass) change += Core.NotAloneFactor;
                    if (Core.IsInEditor)
                        foreach (PartCrewManifest p in ShipConstruction.ShipManifest.PartManifests)
                        {
                            ModuleKerbalHealth mkh = p.PartInfo.partPrefab.FindModuleImplementing<ModuleKerbalHealth>();
                            if (ModuleKerbalHealth.IsModuleApplicable(p, pcm))
                            {
                                change += mkh.hpChangePerDay;
                                if (mkh.hpMarginalChangePerDay > 0)
                                    khs.LastMarginalPositiveChange += mkh.hpMarginalChangePerDay;
                                else if (mkh.hpMarginalChangePerDay < 0)
                                    khs.LastMarginalNegativeChange -= mkh.hpMarginalChangePerDay;
                            }
                        }
                    else foreach (Part p in pcm.seat.vessel.Parts)
                        {
                            ModuleKerbalHealth mkh = p.FindModuleImplementing<ModuleKerbalHealth>();
                            if (ModuleKerbalHealth.IsModuleApplicable(mkh, pcm))
                            {
                                change += mkh.hpChangePerDay;
                                if (mkh.hpMarginalChangePerDay > 0)
                                    khs.LastMarginalPositiveChange += mkh.hpMarginalChangePerDay;
                                else if (mkh.hpMarginalChangePerDay < 0)
                                    khs.LastMarginalNegativeChange -= mkh.hpMarginalChangePerDay;
                            }
                        }
                }
                khs.LastChange = change;
            }
            else if (pcm.rosterStatus == ProtoCrewMember.RosterStatus.Assigned && !isKerbalLoaded(pcm))
            {
                //Core.Log(pcm.name + " is assigned, but not loaded. Seat: " + pcm?.seat + " (id " + pcm?.seatIdx + "). Using last cached HP change: " + khs.LastHPChange);
                change = khs.LastChange;
            }
            else if (!Core.IsInEditor && (pcm.rosterStatus == ProtoCrewMember.RosterStatus.Available)) change = Core.KSCFactor;
            Core.Log("Marginal change: +" + khs.LastMarginalPositiveChange + "%, -" + khs.LastMarginalPositiveChange + "%.");
            return change + khs.MarginalChange;
        }

        public void Update(double interval)
        {
            Core.Log("Updating " + Name + "'s health.");
            HP += HealthChangePerDay(PCM) / 21600 * interval;
            if (HP <= Core.DeathHealth * MaxHP)
            {
                Core.Log(Name + " dies due to having " + HP + " health.");
                if (PCM.seat != null) PCM.seat.part.RemoveCrewmember(PCM);
                PCM.rosterStatus = ProtoCrewMember.RosterStatus.Dead;
                ScreenMessages.PostScreenMessage(Name + " dies of poor health!");
            }
            if (Condition == HealthCondition.OK && HP <= Core.ExhaustionStartHealth * MaxHP)
            {
                Condition = HealthCondition.Exhausted;
                ScreenMessages.PostScreenMessage(Name + " is exhausted!");
            }
            if (Condition == HealthCondition.Exhausted && HP >= Core.ExhaustionEndHealth * MaxHP)
            {
                Condition = HealthCondition.OK;
                ScreenMessages.PostScreenMessage(Name + " has revived.");
            }
        }

        public ConfigNode ConfigNode
        {
            get
            {
                ConfigNode n = new ConfigNode("KerbalHealthStatus");
                n.AddValue("name", Name);
                n.AddValue("health", HP);
                n.AddValue("condition", Condition);
                if (Condition == HealthCondition.Exhausted) n.AddValue("trait", Trait);
                if (LastChange != 0) n.AddValue("lastChange", LastChange);
                Core.Log(Name + "'s last marginal changes were: +" + LastMarginalPositiveChange + "%, -" + LastMarginalNegativeChange + "%.");
                if (LastMarginalPositiveChange != 0) n.AddValue("lastMarginalPositiveChange", LastMarginalPositiveChange);
                if (LastMarginalNegativeChange != 0) n.AddValue("lastMarginalNegativeChange", LastMarginalNegativeChange);
                if (IsOnEVA) n.AddValue("onEva", true);
                return n;
            }
            set
            {
                Name = value.GetValue("name");
                HP = Double.Parse(value.GetValue("health"));
                Condition = (KerbalHealthStatus.HealthCondition)Enum.Parse(typeof(HealthCondition), value.GetValue("condition"));
                if (Condition == HealthCondition.Exhausted) Trait = value.GetValue("trait");
                try { LastChange = double.Parse(value.GetValue("lastChange")); }
                catch (Exception) { LastChange = 0; }
                try { LastMarginalPositiveChange = double.Parse(value.GetValue("lastMarginalPositiveChange")); }
                catch (Exception) { LastMarginalPositiveChange = 0; }
                try { LastMarginalNegativeChange = double.Parse(value.GetValue("lastMarginalNegativeChange")); }
                catch (Exception) { LastMarginalNegativeChange = 0; }
                Core.Log(Name + "'s loaded last marginal changes are: +" + LastMarginalPositiveChange + "%, -" + LastMarginalNegativeChange + "%.");
                try { IsOnEVA = bool.Parse(value.GetValue("onEva")); }
                catch (Exception) { IsOnEVA = false; }
            }
        }

        public override bool Equals(object obj)
        {
            return ((KerbalHealthStatus)obj).Name.Equals(Name);
        }

        public override int GetHashCode()
        {
            return base.GetHashCode();
        }

        public KerbalHealthStatus() { }

        public KerbalHealthStatus(string name)
        {
            Name = name;
            HP = MaxHP;
        }

        public KerbalHealthStatus(string name, double health)
        {
            Name = name;
            HP = health;
        }

        public KerbalHealthStatus(ConfigNode node)
        {
            ConfigNode = node;
        }
    }
}
