﻿using System;
using System.Collections.Generic;
using KSP.Localization;

namespace KerbalHealth
{
    public class ModuleKerbalHealth : PartModule, IResourceConsumer
    {
        [KSPField]
        public string title = "";  // Module title displayed in right-click menu (empty string for auto)

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true)]
        public uint id = 0;

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

        [KSPField]
        public float complexity = 0;  // 0 if no training needed for this part, 1 for standard training complexity

        [KSPField(isPersistant = true)]
        public bool isActive = true;  // If not alwaysActive, this determines if the module is active

        [KSPField(isPersistant = true)]
        public bool starving = false;  // Determines if the module is disabled due to the lack of the resource

        [KSPField(guiName = "#KH_Module_ecPersec", guiActive = true, guiActiveEditor = true)]
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
            Core.Log("Complexity of " + part.partName + ": " + complexity.ToString("P0"), Core.LogLevel.Important);
            if ((complexity != 0) && (id == 0)) id = part.persistentId;
            if (IsAlwaysActive)
            {
                isActive = true;
                Events["OnToggleActive"].guiActive = false;
                Events["OnToggleActive"].guiActiveEditor = false;
            }
            UpdateGUIName();
            if (Core.IsInEditor && (resource == "ElectricCharge")) 
                ecPerSec = resourceConsumption + resourceConsumptionPerKerbal * CappedAffectedCrewCount;
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
                if (recuperation > 0) return Localizer.Format("#KH_Module_type1");//"R&R"
                if (decay > 0) return Localizer.Format("#KH_Module_type2");//"Health Poisoning"
                switch (multiplyFactor.ToLower())
                {
                    case "stress": return Localizer.Format("#KH_Module_type3");  //"Stress Relief"
                    case "confinement": return Localizer.Format("#KH_Module_type4");//"Comforts"
                    case "loneliness": return Localizer.Format("#KH_Module_type5");//"Meditation"
                    case "microgravity": return (multiplier <= 0.25) ? Localizer.Format("#KH_Module_type6") : Localizer.Format("#KH_Module_type7");//"Paragravity""Exercise Equipment"
                    case "connected": return Localizer.Format("#KH_Module_type8");//"TV Set"
                    case "sickness": return Localizer.Format("#KH_Module_type9");//"Sick Bay"
                }
                if (space > 0) return Localizer.Format("#KH_Module_type10");//"Living Space"
                if (shielding > 0) return Localizer.Format("#KH_Module_type11");//"RadShield"
                if (radioactivity > 0) return Localizer.Format("#KH_Module_type12");//"Radiation"
                return Localizer.Format("#KH_Module_title");//"Health Module"
            }
            set => title = value;
        }

        void UpdateGUIName() => Events["OnToggleActive"].guiName = (isActive ? Localizer.Format("#KH_Module_Disable") : Localizer.Format("#KH_Module_Enable")) + Title;//"Disable ""Enable "
        
        [KSPEvent(name = "OnToggleActive", guiActive = true, guiName = "Toggle Health Module", guiActiveEditor = true)]
        public void OnToggleActive()
        {
            isActive = IsAlwaysActive || !isActive;
            UpdateGUIName();
        }

        public override string GetInfo()
        {
            string res = "";
            if (hpChangePerDay != 0) res = Localizer.Format("#KH_Module_info1", hpChangePerDay.ToString("F1"));//"\nHealth points: " +  + "/day"
            if (recuperation != 0) res += Localizer.Format("#KH_Module_info2", recuperation.ToString("F1"));//"\nRecuperation: " +  + "%/day"
            if (decay != 0) res += Localizer.Format("#KH_Module_info3", decay.ToString("F1"));//"\nHealth decay: " +  + "%/day"
            if (multiplier != 1) res += Localizer.Format("#KH_Module_info4", multiplier.ToString("F2"),multiplyFactor);//"\n" +  + "x " + 
            if (crewCap > 0) res += Localizer.Format("#KH_Module_info5", crewCap,(crewCap != 1 ? Localizer.Format("#KH_Module_info5_s") : ""));//" for up to " +  + " kerbal" + "s"
            if (space != 0) res += Localizer.Format("#KH_Module_info6",space.ToString("F1"));//"\nSpace: " + 
            if (resourceConsumption != 0) res += Localizer.Format("#KH_Module_info7", ResourceDefinition.abbreviation,resourceConsumption.ToString("F2"));//"\n" +  + ": " +  + "/sec."
            if (resourceConsumptionPerKerbal != 0) res += Localizer.Format("#KH_Module_info8", ResourceDefinition.abbreviation,resourceConsumptionPerKerbal.ToString("F2"));//"\n" +  + " per Kerbal: " +  + "/sec."
            if (shielding != 0) res += Localizer.Format("#KH_Module_info9", shielding.ToString("F1"));//"\nShielding rating: " + 
            if (radioactivity != 0) res += Localizer.Format("#KH_Module_info10", radioactivity.ToString("N0"));//"\nRadioactive emission: " +  + "/day"
            if (complexity != 0) res += Localizer.Format("#KH_Module_info11", (complexity * 100).ToString("N0"));// "\nTraining complexity: " + (complexity * 100).ToString("N0") + "%"
            if (res == "") return "";
            return  Localizer.Format("#KH_Module_typetitle", Title)+ res;//"Module type: " + 
        }
    }
}
