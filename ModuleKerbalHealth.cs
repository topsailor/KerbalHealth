﻿using System;
using System.Collections.Generic;

namespace KerbalHealth
{
    public class ModuleKerbalHealth : PartModule, IResourceConsumer
    {
        [KSPField]
        public string title = "";  // Module title displayed in right-click menu (empty string for auto)

        [KSPField]
        public float hpChangePerDay = 0;  // How many raw HP per day every affected kerbal gains

        [KSPField]
        public float recuperation = 0;  // Will increase HP by this % of (MaxHP - HP) per day

        [KSPField]
        public float decay = 0;  // Will decrease by this % of (HP - MinHP) per day

        [KSPField]
        public bool partCrewOnly = false;  // Does the module affect health of only crew in this part or the entire vessel?

        [KSPField]
        public string multiplyFactor = "All";  // Name of factor whose effect is multiplied

        [KSPField]
        public float multiplier = 1;  // How the factor is changed (e.g., 0.5 means factor's effect is halved)

        [KSPField]
        public int crewCap = 0;  // Max crew this module's multiplier applies to without penalty, 0 for unlimited (a.k.a. free multiplier)

        [KSPField]
        public double space = 0;  // Points of living space provided by the part (used to calculate Confinement factor)

        [KSPField]
        public float shielding = 0;  // Number of halving-thicknesses

        [KSPField]
        public float radioactivity = 0;  // Radioactive emission, bananas/day

        [KSPField]
        public string resource = "ElectricCharge";  // Determines, which resource is consumed by the module

        [KSPField]
        public float resourceConsumption = 0;  // Flat EC consumption (units per second)

        [KSPField]
        public float resourceConsumptionPerKerbal = 0;  // EC consumption per affected kerbal (units per second)

        [KSPField(isPersistant = true)]
        public bool isActive = true;  // If not alwaysActive, this determines if the module is active

        [KSPField(isPersistant = true)]
        public bool starving = false;  // Determines if the module is disabled due to the lack of the resource

        [KSPField(guiActive = true, guiActiveEditor = true)]
        public float ecPerSec = 0;

        double lastUpdated;

        public HealthFactor MultiplyFactor
        {
            get => Core.GetHealthFactor(multiplyFactor);
            set => multiplyFactor = value.Name;
        }

        public bool IsAlwaysActive => (resourceConsumption == 0) && (resourceConsumptionPerKerbal == 0);

        public bool IsModuleActive => IsAlwaysActive || (isActive && (!Core.IsInEditor || KerbalHealthEditorReport.HealthModulesEnabled) && !starving);

        /// <summary>
        /// Returns total # of kerbals affected by this module
        /// </summary>
        public int TotalAffectedCrewCount
        {
            get
            {
                if (Core.IsInEditor)
                    if (partCrewOnly)
                    {
                        int r = 0;
                        foreach (ProtoCrewMember pcm in ShipConstruction.ShipManifest.GetPartCrewManifest(part.craftID).GetPartCrew())
                            if (pcm != null) r++;
                        Core.Log(r + " kerbal(s) found in " + part?.name + ".");
                        return r;
                    }
                    else return ShipConstruction.ShipManifest.CrewCount;
                else if (partCrewOnly) return part.protoModuleCrew.Count;
                else return vessel.GetCrewCount();
            }
        }

        /// <summary>
        /// Returns # of kerbals affected by this module, capped by crewCap
        /// </summary>
        public int CappedAffectedCrewCount => crewCap > 0 ? Math.Min(TotalAffectedCrewCount, crewCap) : TotalAffectedCrewCount;

        public List<PartResourceDefinition> GetConsumedResources() => resourceConsumption != 0 ? new List<PartResourceDefinition>() { ResourceDefinition } : new List<PartResourceDefinition>();

        PartResourceDefinition ResourceDefinition
        {
            get => PartResourceLibrary.Instance.GetDefinition(resource);
            set => resource = value?.name;
        }

        public double RecuperationPower => crewCap > 0 ? recuperation * Math.Min((double)crewCap / TotalAffectedCrewCount, 1) : recuperation;

        public double DecayPower => crewCap > 0 ? decay * Math.Min((double)crewCap / TotalAffectedCrewCount, 1) : decay;

        public override void OnStart(StartState state)
        {
            Core.Log("ModuleKerbalHealth.OnStart(" + state + ") for " + part.name);
            base.OnStart(state);
            if (IsAlwaysActive)
            {
                isActive = true;
                Events["OnToggleActive"].guiActive = false;
                Events["OnToggleActive"].guiActiveEditor = false;
            }
            UpdateGUIName();
            if (Core.IsInEditor && (resource == "ElectricCharge")) ecPerSec = resourceConsumption + resourceConsumptionPerKerbal * CappedAffectedCrewCount;
            lastUpdated = Planetarium.GetUniversalTime();
        }

        public void FixedUpdate()
        {
            if (Core.IsInEditor || !Core.ModEnabled) return;
            double time = Planetarium.GetUniversalTime();
            if (isActive && ((resourceConsumption != 0) || (resourceConsumptionPerKerbal != 0)))
            {
                ecPerSec = resourceConsumption + resourceConsumptionPerKerbal * CappedAffectedCrewCount;
                double res = ecPerSec * (time - lastUpdated), res2;
                if (resource != "ElectricCharge") ecPerSec = 0;
                starving = (res2 = vessel.RequestResource(part, ResourceDefinition.id, res, false)) * 2 < res;
                if (starving) Core.Log(Title + " Module is starving of " + resource + " (" + res + " needed, " + res2 + " provided).");
            }
            else ecPerSec = 0;
            lastUpdated = time;
        }

        public string Title
        {
            get
            {
                if (title != "") return title;
                if (recuperation > 0) return "R&R";
                if (decay > 0) return "Health Poisoning";
                switch (multiplyFactor.ToLower())
                {
                    case "confinement": return "Comforts";
                    case "loneliness": return "Meditation";
                    case "microgravity": return (multiplier <= 0.25) ? "Paragravity" : "Exercise Equipment";
                    case "connected": return "TV Set";
                    case "sickness": return "Sick Bay";
                }
                if (space > 0) return "Living Space";
                if (shielding > 0) return "RadShield";
                if (radioactivity > 0) return "Radiation";
                return "Health Module";
            }
            set => title = value;
        }

        void UpdateGUIName() => Events["OnToggleActive"].guiName = (isActive ? "Disable " : "Enable ") + Title;
        
        [KSPEvent(name = "OnToggleActive", guiActive = true, guiName = "Toggle Health Module", guiActiveEditor = true)]
        public void OnToggleActive()
        {
            isActive = IsAlwaysActive || !isActive;
            UpdateGUIName();
        }

        public override string GetInfo()
        {
            string res = "";
            if (hpChangePerDay != 0) res = "\nHealth points: " + hpChangePerDay.ToString("F1") + "/day";
            if (recuperation != 0) res += "\nRecuperation: " + recuperation.ToString("F1") + "%/day";
            if (decay != 0) res += "\nHealth decay: " + decay.ToString("F1") + "%/day";
            if (multiplier != 1) res += "\n" + multiplier.ToString("F2") + "x " + multiplyFactor;
            if (crewCap > 0) res += " for up to " + crewCap + " kerbal" + (crewCap != 1 ? "s" : "");
            if (space != 0) res += "\nSpace: " + space.ToString("F1");
            if (resourceConsumption != 0) res += "\n" + ResourceDefinition.abbreviation + ": " + resourceConsumption.ToString("F2") + "/sec.";
            if (resourceConsumptionPerKerbal != 0) res += "\n" + ResourceDefinition.abbreviation + " per Kerbal: " + resourceConsumptionPerKerbal.ToString("F2") + "/sec.";
            if (shielding != 0) res += "\nShielding rating: " + shielding.ToString("F1");
            if (radioactivity != 0) res += "\nRadioactive emission: " + radioactivity.ToString("N0") + "/day";
            if (res == "") return "";
            return "Module type: " + Title + res;
        }
    }
}
