﻿using System;
using System.Collections.Generic;
using UnityEngine;

namespace KerbalHealth
{
    public class ModuleKerbalHealth : PartModule, IResourceConsumer
    {
        [KSPField]
        public float hpChangePerDay = 0;  // How many raw HP per day every affected kerbal gains

        [KSPField]
        public float hpMarginalChangePerDay = 0;  // If >0, will increase HP by this % of (MaxHP - HP). If <0, will decrease by this % of (HP - MinHP)

        [KSPField]
        public bool partCrewOnly = false;  // Does the module affect health of only crew in this part or the entire vessel?

        [KSPField]
        public float ecConsumption = 0;  // Flat EC consumption (units per second)

        [KSPField]
        public float ecConsumptionPerKerbal = 0;  // EC consumption per affected kerbal (units per second)

        [KSPField]
        public bool alwaysActive = false;  // Is the module's effect (and consumption) always active or togglable in-flight

        [KSPField(isPersistant = true)]
        public bool isActive = true;  // If not alwaysActive, this determines if the module is active

        double lastUpdated;

        public bool IsModuleActive()
        { return alwaysActive || isActive; }

        public static bool IsModuleActive(ModuleKerbalHealth mkh)
        { return (mkh != null) && mkh.IsModuleActive(); }

        public static bool IsModuleApplicable(PartCrewManifest part, ProtoCrewMember pcm)
        {
            ModuleKerbalHealth mkh = part?.PartInfo?.partPrefab?.FindModuleImplementing<ModuleKerbalHealth>();
            return IsModuleActive(mkh) && (!mkh.partCrewOnly || part.Contains(pcm));
        }

        public static bool IsModuleApplicable(ModuleKerbalHealth mkh, ProtoCrewMember pcm)
        {
            return IsModuleActive(mkh) && (!mkh.partCrewOnly || mkh.part.protoModuleCrew.Contains(pcm));
        }

        public int AffectedCrewCount
        {
            get
            {
                if (Core.IsInEditor)
                    if (partCrewOnly)
                    {
                        foreach (PartCrewManifest pcm in ShipConstruction.ShipManifest.PartManifests)
                            foreach (ModuleKerbalHealth mkh in pcm.PartInfo.partPrefab.FindModulesImplementing<ModuleKerbalHealth>())
                                if (mkh == this) return pcm.GetPartCrew().Length;
                        return 0;
                    }
                    else return ShipConstruction.ShipManifest.CrewCount;
                if (partCrewOnly) return part.protoModuleCrew.Count;
                else return vessel.GetCrewCount();
            }
        }

        public List<PartResourceDefinition> GetConsumedResources()
        {
            if (ecConsumption != 0) return new List<PartResourceDefinition>() { PartResourceLibrary.Instance.GetDefinition("ElectricCharge") };
            else return new List<PartResourceDefinition>();
        }

        public override void OnStart(StartState state)
        {
            Core.Log("ModuleKerbalHealth.OnStart (" + state + ")");
            base.OnStart(state);
            lastUpdated = Planetarium.GetUniversalTime();
        }

        public void FixedUpdate()
        {
            if (Core.IsInEditor) return;
            double time = Planetarium.GetUniversalTime();
            if (IsModuleActive() && ((ecConsumption != 0) || (ecConsumptionPerKerbal != 0)) && (TimeWarp.CurrentRate == 1))
            {
                Core.Log(AffectedCrewCount + " crew affected by this part.");
                double ec = (ecConsumption + ecConsumptionPerKerbal * AffectedCrewCount) * (time - lastUpdated), ec2;
                if ((ec2 = vessel.RequestResource(part, PartResourceLibrary.Instance.GetDefinition("ElectricCharge").id, ec, false)) * 2 < ec)
                {
                    Core.Log("Module shut down due to lack of EC (" + ec + " needed, " + ec2 + " provided).");
                    ScreenMessages.PostScreenMessage("Kerbal Health Module shut down due to lack of EC.");
                    isActive = false;
                }
            }
            else Core.Log("Module is active: " + IsModuleActive() + "\nEC consumption: " + ecConsumption + "\nEC consumption per kerbal: " + ecConsumptionPerKerbal + "\nTime warp: " + TimeWarp.CurrentRate);
            lastUpdated = time;
        }

        [KSPEvent(active = true, guiActive = true, name = "OnToggleActive", guiName = "Health Module")]
        public void OnToggleActive()
        {
            isActive = !isActive;
        }

        public override string GetInfo()
        {
            string res = "KerbalHealth Module";
            if (hpChangePerDay != 0) res += "\nHP/day: " + hpChangePerDay.ToString("F1");
            if (hpMarginalChangePerDay != 0) res += "\nMarginal HP/day: " + hpMarginalChangePerDay.ToString("F1") + "%";
            if (partCrewOnly) res += "\nAffects only part crew"; else res += "\nAffects entire vessel";
            if (ecConsumption != 0) res += "\nElectric Charge: " + ecConsumption.ToString("F1") + "/sec.";
            if (ecConsumptionPerKerbal != 0) res += "\nElectric Charge per Kerbal: " + ecConsumptionPerKerbal.ToString("F1") + "/sec.";
            return res;
        }
    }
}