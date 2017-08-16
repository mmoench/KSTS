using System;
using System.Linq;
using UnityEngine;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using StageRecovery;

namespace KSTS
{
    public class PayloadResource
    {
        public string name;
        public double amount;
        public double mass;

        public PayloadResource Clone()
        {
            PayloadResource copy = new PayloadResource();
            copy.name = name;
            copy.amount = amount;
            copy.mass = mass;
            return copy;
        }
    }

    public class PayloadAssembly
    {
        public string id;
        public string name;
        public double mass;
        public double value;
        public int partCount;
        public Part detachmentPart;
        public List<Part> parts = null; // This is only needed for highlighting the assembly.
        public bool containsInvalidParts = false;

        public PayloadAssembly(PayloadAssembly init = null)
        {
            this.parts = new List<Part>();
            if (init != null)
            {
                this.mass = init.mass;
                this.value = init.value;
                this.partCount = init.partCount;
                this.containsInvalidParts = init.containsInvalidParts;
                foreach (Part part in init.parts) this.parts.Add(part);
            }
        }

        // Turns the highlighting of the entire subassembly on or off:
        public void Highlight(bool switchOn)
        {
            foreach (Part part in parts)
            {
                if (switchOn)
                {
                    part.SetHighlightType(Part.HighlightType.AlwaysOn);
                    part.SetHighlightColor(Color.blue);
                }
                else
                {
                    part.Highlight(false);
                    part.SetHighlightType(Part.HighlightType.OnMouseOver);
                    part.SetHighlightColor();
                }
            }
        }
    }

    public class RecordingVesselStats
    {
        public double cost = 0;
        public double mass = 0;
        public bool hasDockingPort = false;
        public bool hasSeperator = false;
        public bool hasCrew = false;
        public bool hasRCS = false;

        public Dictionary<string, PayloadResource> payloadResources;
        public Dictionary<string, PayloadAssembly> payloadAssemblies;
        public List<string> dockingPortTypes;

        // Sometimes the name of the root-part of a vessel is extended by the vessel-name like "Mk1Pod (X-Bird)", this function returns the real part-name in thoes cases:
        public static string SanitizeParteName(string partName)
        {
            return Regex.Replace(partName, @" \(.*\)$", "");
        }

        // Gathers all important current stats for the given vessel (eg its mass, price, etc):
        public static RecordingVesselStats GetStats(FlightRecording recording, Vessel vessel)
        {
            RecordingVesselStats stats = new RecordingVesselStats();
            stats.hasCrew = vessel.GetCrewCount() > 0;
            stats.payloadResources = new Dictionary<string, PayloadResource>();
            stats.dockingPortTypes = new List<string>();

            foreach (Part part in vessel.parts)
            {
                string partName = SanitizeParteName(part.name);

                // Check for modules which enable the vessel to perform certain actions:
                List<ModuleDockingNode> dockingNodes = part.FindModulesImplementing<ModuleDockingNode>();
                if (dockingNodes.Count() > 0)
                {
                    stats.hasDockingPort = true;
                    foreach (ModuleDockingNode dockingNode in dockingNodes)
                    {
                        if (!stats.dockingPortTypes.Contains(dockingNode.nodeType.ToString())) stats.dockingPortTypes.Add(dockingNode.nodeType.ToString()); // Docking-nodes have differnt sizes, like "node0", "node1", etc
                    }
                }
                if (part.FindModulesImplementing<ModuleDecouple>().Count() > 0) stats.hasSeperator = true;
                if (part.FindModulesImplementing<ModuleAnchoredDecoupler>().Count() > 0) stats.hasSeperator = true;
                if (part.FindModulesImplementing<ModuleRCS>().Count() > 0) stats.hasRCS = true;

                // Mass of the part and its resources:
                stats.mass += part.mass + part.resourceMass;

                // Sum up all the parts resources:
                double resourceCost = 0;
                double resourceCostMax = 0;
                foreach (PartResource resource in part.Resources)
                {
                    PartResourceDefinition resourceDefinition = null;
                    if (!KSTS.resourceDictionary.TryGetValue(resource.resourceName.ToString(), out resourceDefinition)) Debug.LogError("[KSTS] RecordingVesselStats.GetStats(): resource '" + resource.resourceName.ToString() + "' not found in dictionary");
                    else
                    {
                        // Cost:
                        resourceCost += (double)(resource.amount * resourceDefinition.unitCost);
                        resourceCostMax += (double)(resource.maxAmount * resourceDefinition.unitCost);

                        // Track remaining amout for payload delivery (only resources which have a weight):
                        if (resourceDefinition.density > 0 && resource.amount > 0)
                        {
                            PayloadResource payloadResource = null;
                            if (stats.payloadResources.TryGetValue(resource.resourceName.ToString(), out payloadResource))
                            {
                                stats.payloadResources.Remove(resource.resourceName.ToString());
                                payloadResource.amount += resource.amount;
                            }
                            else
                            {
                                payloadResource = new PayloadResource();
                                payloadResource.amount = resource.amount;
                                payloadResource.name = resource.resourceName.ToString();
                                payloadResource.mass = resourceDefinition.density;
                            }
                            stats.payloadResources.Add(resource.resourceName.ToString(), payloadResource);
                        }
                    }
                }
                stats.cost += resourceCost;

                // The cost of the part is only available in the AvailablePart-class:
                AvailablePart availablePart = null;
                if (!KSTS.partDictionary.TryGetValue(partName, out availablePart)) Debug.LogError("[KSTS] RecordingVesselStats.GetStats(): part '" + partName + "' not found in dictionary");
                else
                {
                    // The cost of the part already includes the resource-costs, when completely filled:
                    double dryCost = availablePart.cost - resourceCostMax;
                    stats.cost += dryCost;
                }
            }

            // Find all crewed parts:
            List<string> crewedPartIds = new List<string>();
            foreach (ProtoCrewMember crewMember in vessel.GetVesselCrew())
            {
                if (crewMember.seat == null || crewMember.seat.part == null) continue;
                if (crewedPartIds.Contains(crewMember.seat.part.flightID.ToString())) continue;
                crewedPartIds.Add(crewMember.seat.part.flightID.ToString());
            }

            // Find all valid subassemblies, which can be detached from the control-part:
            stats.payloadAssemblies = new Dictionary<string, PayloadAssembly>();
            stats.FindPayloadAssemblies(recording, vessel.rootPart, null, crewedPartIds);

            return stats;
        }

        // Recursively finds all detachable assemblies, which are attached as children to the given part and
        // stores them in "payloadAssemblies":
        // TODO: Maybe we should look at the direction dockung-ports and decouples are facing. If they stay on the ship, thes should not count as payload, just as seperators should not get counted as well.
        protected PayloadAssembly FindPayloadAssemblies(FlightRecording recording, Part part, Part parent, List<string> crewedPartIds)
        {
            PayloadAssembly assembly = new PayloadAssembly();

            // Iterate through all attached parts:
            List<Part> attachedParts = new List<Part>(part.children);
            if (part.parent != null) attachedParts.Add(part.parent);
            foreach (Part attachedPart in attachedParts)
            {
                if (parent != null && attachedPart.flightID == parent.flightID) continue; // Ignore the part we came from in the iteration.
                PayloadAssembly subassembly = this.FindPayloadAssemblies(recording, attachedPart, part, crewedPartIds);
                if (subassembly == null) continue;
                assembly.mass += subassembly.mass;
                assembly.partCount += subassembly.partCount;
                assembly.parts.AddRange(subassembly.parts);
                assembly.value += subassembly.value;
                if (subassembly.containsInvalidParts) assembly.containsInvalidParts = true;
            }

            if (assembly.partCount > 0 && (
                part.FindModulesImplementing<ModuleDockingNode>().Count() > 0 ||
                part.FindModulesImplementing<ModuleDecouple>().Count() > 0 ||
                part.FindModulesImplementing<ModuleAnchoredDecoupler>().Count() > 0
            ))
            {
                // This is a seperator/dockingport, add all children as valid, detachable subassembly (excluding the seperator, providing the subassembly is actually valid):
                if (!assembly.containsInvalidParts)
                {
                    // Create a copy of the assembly, which will alow us to use the original object for assemblies higher up the recursion-chain:
                    PayloadAssembly payloadAssembly = new PayloadAssembly(assembly);

                    AvailablePart availablePart = null;
                    if (KSTS.partDictionary.TryGetValue(part.name.ToString(), out availablePart)) payloadAssembly.name = availablePart.title.ToString();
                    else payloadAssembly.name = part.name.ToString();
                    payloadAssembly.detachmentPart = part;
                    payloadAssembly.id = part.flightID.ToString();
                    if (!this.payloadAssemblies.ContainsKey(payloadAssembly.id)) this.payloadAssemblies.Add(payloadAssembly.id, payloadAssembly);
                }
                else
                {
                    assembly = new PayloadAssembly();
                }
            }

            // Check if this part was active at some point during the flight, making this an invalid subassembly:
            if (recording.usedPartIds.Contains(part.flightID.ToString())) assembly.containsInvalidParts = true;

            // Check if the part has a crewmember and thous can not be used as payload:
            if (crewedPartIds.Contains(part.flightID.ToString())) assembly.containsInvalidParts = true;

            // Determine the cost of the current part:
            double partCost = 0;
            string partName = SanitizeParteName(part.name);
            if (KSTS.partDictionary.ContainsKey(partName))
            {
                partCost += KSTS.partDictionary[partName].cost; // Includes resource-costs
                foreach (PartResource resource in part.Resources)
                {
                    // Determine the real value of the part with the current amount of resources inside:
                    if (!KSTS.resourceDictionary.ContainsKey(resource.resourceName)) continue;
                    partCost -= KSTS.resourceDictionary[resource.resourceName].unitCost * resource.maxAmount;
                    partCost += KSTS.resourceDictionary[resource.resourceName].unitCost * resource.amount;
                }
            }

            // Add this part's mass for subassemblies higher up the recursion:
            assembly.mass += part.mass + part.resourceMass;
            assembly.partCount += 1;
            assembly.parts.Add(part);
            assembly.value += partCost;
            return assembly;
        }
    }

    public enum FlightRecordingStatus { PRELAUNCH=1, ASCENDING=2, DESCENDING=3 };
    public class FlightRecording : Saveable
    {
        public FlightRecordingStatus status;
        public string profileName = "";
        public MissionProfileType missionType;

        public double startTime = 0;
        public double deploymentTime = 0;
        public double launchCost = 0;
        public double launchMass = 0;

        public string launchBodyName = "";
        public double minAltitude = 0;
        public double maxAltitude = 0;
        public double payloadMass = 0;

        public bool mustReturn = false;

        public List<string> usedPartIds = null;
        public List<string> dockingPortTypes = null;

        // The following attributes are not persistent, they are just used for easier access and updated when calling "Update":
        private Vessel vessel = null;
        private RecordingVesselStats currentStats = null;

        public FlightRecording(Vessel vessel = null)
        {
            usedPartIds = new List<string>();
            if (!vessel) return; // Used when creating a recording from a config-node.

            status = FlightRecordingStatus.PRELAUNCH;
            startTime = Planetarium.GetUniversalTime();
            profileName = vessel.vesselName.ToString();

            // Save the minimum altitude we need for a stable orbit as well as the launch-body's name:
            launchBodyName = vessel.mainBody.bodyName;
            FloatCurve pressureCurve = vessel.mainBody.atmospherePressureCurve;
            for (int i = pressureCurve.Curve.length - 1; i >= 0; i--)
            {
                if (pressureCurve.Curve[i].value == 0)
                {
                    minAltitude = pressureCurve.Curve[i].time;
                    break;
                }
            }

            Update(vessel);
        }

        public static FlightRecording CreateFromConfigNode(ConfigNode node)
        {
            FlightRecording recording = new FlightRecording();
            return (FlightRecording)CreateFromConfigNode(node, recording);
        }

        public void Update(Vessel vessel)
        {
            this.currentStats = RecordingVesselStats.GetStats(this, vessel);
            this.vessel = vessel;

            // Set launch-values:
            if (this.status == FlightRecordingStatus.PRELAUNCH)
            {
                this.launchCost = this.currentStats.cost;
                this.launchMass = this.currentStats.mass;
                if (this.currentStats.hasCrew) this.mustReturn = true;
                else this.mustReturn = false;
            }

            // When in orbit, set the maximum altitude for future missions:
            if (this.status == FlightRecordingStatus.ASCENDING)
            {
                if (vessel.situation == Vessel.Situations.ORBITING && vessel.orbit.referenceBody.bodyName == launchBodyName)
                {
                    this.maxAltitude = vessel.orbit.PeA; // Current periapsis
                }
                else
                {
                    this.maxAltitude = 0;
                }
            }
        }

        // Checks if the vessel can perform the given mission:
        public bool CanPerformMission(MissionProfileType missionType)
        {
            if (this.currentStats == null) return false; // This should not happen.
            switch (missionType)
            {
                case MissionProfileType.TRANSPORT:
                    if (this.currentStats.hasDockingPort && this.currentStats.hasRCS) return true;
                    break;
                case MissionProfileType.DEPLOY:
                    if (this.currentStats.payloadAssemblies.Count > 0) return true;
                    break;
                default:
                    throw new Exception("invalid mission-type");
            }
            return false;
        }

        // Checks if the vessel can deploy its payload in the current situation:
        public bool CanDeploy()
        {
            if (this.status != FlightRecordingStatus.ASCENDING) return false;
            if (this.minAltitude <= 0 || this.maxAltitude <= 0) return false; // The vessel must be in a stable orbit.
            return true;
        }

        public bool CanFinish()
        {
            if (this.status != FlightRecordingStatus.DESCENDING) return false;
            if (this.payloadMass <= 0) return false;
            if (this.vessel.mainBody.bodyName != this.launchBodyName) return false;
            if (this.mustReturn)
            {
                // The vessel must have landed on the planet from where it came:
                if (this.vessel.situation == Vessel.Situations.LANDED || this.vessel.situation == Vessel.Situations.SPLASHED) return true;
                return false;
            }
            else
            {
                // All conditions met for one-way mission:
                return true;
            }
        }

        public void DeployPayloadResources(Dictionary<string, double> requestedResources)
        {
            double dumpedMass = 0;
            double dumpedFunds = 0;
            if (!this.CanPerformMission(MissionProfileType.TRANSPORT)) return;

            // Try to dump the requested amount of resources from the vessel:
            foreach (var item in requestedResources)
            {
                string requestedName = item.Key;
                double requestedAmount = item.Value;

                PayloadResource payloadResource = null;
                if (!this.currentStats.payloadResources.TryGetValue(requestedName, out payloadResource)) continue;
                if (requestedAmount > payloadResource.amount) requestedAmount = payloadResource.amount;
                if (requestedAmount < 0) requestedAmount = 0;

                // Find parts to dump the resources from:
                double amountToDump = requestedAmount;
                foreach (Part part in vessel.parts)
                {
                    if (amountToDump <= 0) break;
                    double dumpedAmount = part.RequestResource(requestedName, amountToDump);
                    if (dumpedAmount == 0) continue;
                    Debug.Log("[KSTS] dumped " + dumpedAmount.ToString() + " of " + requestedName.ToString() + " from " + part.name.ToString());
                    amountToDump -= dumpedAmount;
                    dumpedMass += dumpedAmount * payloadResource.mass;
                    if (KSTS.resourceDictionary.ContainsKey(payloadResource.name)) dumpedFunds += dumpedAmount * KSTS.resourceDictionary[payloadResource.name].unitCost;
                }
            }

            // If there was something dumped, we can move on to the next stage:
            if (dumpedMass > 0)
            {
                this.payloadMass = dumpedMass;
                this.missionType = MissionProfileType.TRANSPORT;
                this.deploymentTime = Planetarium.GetUniversalTime();
                this.status = FlightRecordingStatus.DESCENDING;
                this.dockingPortTypes = this.currentStats.dockingPortTypes;

                // The weight and value of the payload should not counted in the vessel's stats:
                this.launchMass -= dumpedMass;
                this.launchCost -= Math.Round(dumpedFunds);
            }
        }

        public void DeployPayloadAssembly(List<PayloadAssembly> payloadAssemblies)
        {
            if (payloadAssemblies.Count < 1) return;

            // Sort the assemblies from largest to smallest, to avoid problems with detaching assemblies that contain other assemblies on the list:
            payloadAssemblies.Sort((x, y) => y.partCount.CompareTo(x.partCount));

            // Deploy all selected payloads:
            double deployedMass = 0;
            double deployedFunds = 0;
            foreach (PayloadAssembly payloadAssembly in payloadAssemblies)
            {
                if (!payloadAssembly.detachmentPart || !this.vessel) continue;
                if (!this.vessel.parts.Contains(payloadAssembly.detachmentPart)) continue; // The subassembly was probably already detached together with a bigger one.
                payloadAssembly.Highlight(false); // Turn off highlighting, in case it was on.

                foreach (ModuleDockingNode dockingModule in payloadAssembly.detachmentPart.FindModulesImplementing<ModuleDockingNode>())
                {
                    dockingModule.Decouple();
                }
                foreach (ModuleDecouple decoupleModule in payloadAssembly.detachmentPart.FindModulesImplementing<ModuleDecouple>())
                {
                    decoupleModule.Decouple();
                }
                foreach (ModuleAnchoredDecoupler decoupleModule in payloadAssembly.detachmentPart.FindModulesImplementing<ModuleAnchoredDecoupler>())
                {
                    decoupleModule.Decouple();
                }

                deployedMass += payloadAssembly.mass;
                deployedFunds += payloadAssembly.value;
            }

            // If there was something deployed, we can move on to the next stage:
            if (deployedMass > 0)
            {
                this.payloadMass = deployedMass;
                this.missionType = MissionProfileType.DEPLOY;
                this.deploymentTime = Planetarium.GetUniversalTime();
                this.status = FlightRecordingStatus.DESCENDING;
                this.dockingPortTypes = this.currentStats.dockingPortTypes; // This mission-type doesn't need docking ports, but this way it's not inconsistent.

                // The weight and value of the payload should not counted in the vessel's stats:
                this.launchMass -= deployedMass;
                this.launchCost -= Math.Round(deployedFunds);
            }
        }

        // Returns a list of key-value pairs to display the current stats of the recording on the GUI:
        public List<KeyValuePair<string, string>> GetDisplayAttributes()
        {
            List<KeyValuePair<string, string>> list = new List<KeyValuePair<string, string>>();
            try
            {
                string statusText = "N/A";
                switch (this.status)
                {
                    case FlightRecordingStatus.PRELAUNCH:
                        statusText = "pre-launch";
                        break;
                    case FlightRecordingStatus.ASCENDING:
                        statusText = "ascending";
                        break;
                    case FlightRecordingStatus.DESCENDING:
                        statusText = "descending";
                        break;
                }
                list.Add(new KeyValuePair<string, string>("Mission Phase", statusText + " (" + this.vessel.situation.ToString().Replace("_","-").ToLower() + ")"));
                list.Add(new KeyValuePair<string, string>("Mission Elapsed Time", GUI.FormatDuration(Planetarium.GetUniversalTime() - this.startTime)));

                list.Add(new KeyValuePair<string, string>("Value",
                    this.currentStats.cost.ToString("#,##0") + " / " +
                    this.launchCost.ToString("#,##0 √")
                ));
                list.Add(new KeyValuePair<string, string>("Mass",
                    this.currentStats.mass.ToString("#,##0.00") + " / " +
                    this.launchMass.ToString("#,##0.00 t")
                ));

                if (this.maxAltitude > 0)
                {
                    list.Add(new KeyValuePair<string, string>("Altitude Min / Max",
                        this.minAltitude.ToString("#,##0") + " / " +
                        this.maxAltitude.ToString("#,##0 m")
                    ));
                }
                else list.Add(new KeyValuePair<string, string>("Altitude Min / Max", "- / -"));

                if (this.payloadMass > 0)
                {
                    list.Add(new KeyValuePair<string, string>("Payload", this.payloadMass.ToString("#,##0.00 t")));
                }

                if (this.deploymentTime > 0)
                {
                    list.Add(new KeyValuePair<string, string>("Mission Duration", GUI.FormatDuration(this.deploymentTime - this.startTime)));
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[KSTS] GetDisplayAttributes(): " + e.ToString());
            }
            return list;
        }

        public List<PayloadResource> GetPayloadResources()
        {
            return this.currentStats.payloadResources.Values.ToList<PayloadResource>();
        }

        public List<PayloadAssembly> GetPayloadAssemblies()
        {
            return this.currentStats.payloadAssemblies.Values.ToList<PayloadAssembly>();
        }

        public double GetCurrentVesselValue()
        {
            return this.currentStats.cost;
        }
    }

    public class FlightRecorder
    {
        private static Dictionary<string, FlightRecording> flightRecordings = null;                 // List of all currently running flight-recordings

        private static string timerVesselId = "";                                                   // ID of the vessel last tracked by the timer-function
        private static Dictionary<string, Dictionary<string, double>> timerPartResources = null;  // Part-ID => ResourceName => ResourceValue of all parts tracked by the timer-function

        public static void Initialize()
        {
            if (FlightRecorder.flightRecordings == null) FlightRecorder.flightRecordings = new Dictionary<string, FlightRecording>();
            if (FlightRecorder.timerPartResources == null) FlightRecorder.timerPartResources = new Dictionary<string, Dictionary<string, double>>();
            KSTS.StageRecovered += OnStageRecovered;
        }

        private static void OnStageRecovered(object sender, KSTS.StageRecoveredEventArgs e)
        {
            string parentKey;
            if (KSTS.parentDictionary.TryGetValue(e.Vessel.id.ToString(), out parentKey))
                foreach (var flightRecording in flightRecordings.Where(x => x.Key == parentKey))
                {
                    if(flightRecording.Value.status != FlightRecordingStatus.PRELAUNCH)
                    {
                        flightRecording.Value.launchCost -= e.FundsRecovered;
                    }
                }
        }

        // Removes entries from the recording-list of non-existent vessels (can only happen when someone deletes a vessel
        // during a running recording):
        public static void CollectGarbage()
        {
            // With KSP 1.2 our onLoad method is called before the FlightGlobals are filled, which would result in removing all
            // active recordings after loading a savegame. This workaround isn't really clean, but I don't know how to check if
            // the FlightGlobals are loaded or not.
            if (FlightGlobals.Vessels.Count == 0) return;

            // Build list of all existing vessel-IDs:
            List<string> existingVesselIds = FlightGlobals.Vessels.Select(vessel => vessel.id.ToString()).ToList();

            // Create a list of non-existing vessel-IDs and remove them afterwards:
            foreach (string removeId in flightRecordings.Keys.Except(existingVesselIds).ToList())
            {
                Debug.Log("[KSTS] removing flight recording for missing vessel '" + removeId + "'");
                FlightRecorder.flightRecordings.Remove(removeId);
            }
            foreach (string removeId in KSTS.parentDictionary.Keys.Except(existingVesselIds).ToList())
            {
                KSTS.parentDictionary.Remove(removeId);
            }
        }

        public static void LoadRecordings(ConfigNode node)
        {
            FlightRecorder.flightRecordings.Clear();
            ConfigNode flightRecorderNode = node.GetNode("FlightRecorder");
            if (flightRecorderNode == null) return;

            foreach (ConfigNode flightRecordingNode in flightRecorderNode.GetNodes())
            {
                FlightRecorder.flightRecordings.Add(flightRecordingNode.name, FlightRecording.CreateFromConfigNode(flightRecordingNode));
            }

            FlightRecorder.CollectGarbage(); // Might not work as expected in KSP 1.2, so we added this also to the timer-function.
        }

        public static void SaveRecordings(ConfigNode node)
        {
            ConfigNode flightRecorderNode = node.AddNode("FlightRecorder");
            foreach (KeyValuePair<string, FlightRecording> item in FlightRecorder.flightRecordings)
            {
                flightRecorderNode.AddNode(item.Value.CreateConfigNode(item.Key));
            }
        }

        // Returns the running recording for the given vessel, if one exists:
        public static FlightRecording GetFlightRecording(Vessel vessel)
        {
            FlightRecording recording = null;
            try
            {
                if (FlightRecorder.flightRecordings.TryGetValue(vessel.id.ToString(), out recording))
                {
                    // Update with the current values of the vessel:
                    recording.Update(vessel);
                }
                else if (
                    vessel.situation == Vessel.Situations.PRELAUNCH ||
                    // Sometimes the vessel starts "jumping" on the launchpad when the physics are kicking in,
                    // which is why we also have to count "landed" on the launchpad as pre-launch:
                    (vessel.situation == Vessel.Situations.LANDED && (vessel.landedAt.ToString() == "Runway" || vessel.landedAt.ToString() == "KSC_LaunchPad_Platform"))
                )
                {
                    // No recording found, but vessel is pre-launch and thus eligible for a new recording:
                    recording = new FlightRecording(vessel);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[KSTS] getFlightRecording(): " + e.ToString());
            }
            return recording;
        }

        // Adds the given recording to the list of running recordings:
        public static void StartRecording(Vessel vessel)
        {
            try
            {
                if (FlightRecorder.flightRecordings.ContainsKey(vessel.id.ToString())) throw new Exception("duplicate recording for vessel '" + vessel.id.ToString() + "'");
                FlightRecording recording = new FlightRecording(vessel);
                FlightRecorder.flightRecordings.Add(vessel.id.ToString(), recording);
                recording.status = FlightRecordingStatus.ASCENDING;
            }
            catch (Exception e)
            {
                Debug.LogError("[KSTS] StartRecording(): " + e.ToString());
            }
        }



        // Aborts a running recording:
        public static void CancelRecording(Vessel vessel)
        {
            try
            {
                if (!FlightRecorder.flightRecordings.ContainsKey(vessel.id.ToString())) throw new Exception("vessel '" + vessel.id.ToString() + "' not found in recording-list");
                FlightRecorder.flightRecordings.Remove(vessel.id.ToString());
            }
            catch (Exception e)
            {
                Debug.LogError("[KSTS] CancelRecording(): " + e.ToString());
            }
        }

        // Closes the active recording for the given vessel and creates a mission-profile from it:
        public static void SaveRecording(Vessel vessel)
        {
            try
            {
                FlightRecording recording;
                if (!FlightRecorder.flightRecordings.TryGetValue(vessel.id.ToString(), out recording)) return;
                if (!recording.CanFinish()) return;

                MissionController.CreateMissionProfile(vessel, recording);
                FlightRecorder.flightRecordings.Remove(vessel.id.ToString());
            }
            catch (Exception e)
            {
                Debug.LogError("[KSTS] SaveRecording(): " + e.ToString());
            }
        }

        // Is called every second and keeps track of used parts during a flight-recording:
        public static void Timer()
        {
            try
            {
                // Maybe remove old, invalid running recordings:
                FlightRecorder.CollectGarbage();

                // Check if we are on an vessel which is recording a flight:
                Vessel vessel = FlightGlobals.ActiveVessel;
                if (!vessel) return;
                FlightRecording recording = GetFlightRecording(vessel);
                if (recording == null) return;

                if (vessel.id.ToString() != FlightRecorder.timerVesselId)
                {
                    // The vessel has changed, reset all variables from the last timer-tick:
                    FlightRecorder.timerVesselId = vessel.id.ToString();
                    FlightRecorder.timerPartResources.Clear();
                }

                // Check all parts, if something has changed which makes the part unusable for payload-deployments:
                foreach (Part part in vessel.parts)
                {
                    if (recording.usedPartIds.Contains(part.flightID.ToString())) continue; // Already blocked
                    bool blockThis = false;
                    string partId = part.flightID.ToString();

                    // Check for running engines:
                    foreach (ModuleEngines engineModule in part.FindModulesImplementing<ModuleEngines>())
                    {
                        if (engineModule.GetCurrentThrust() > 0) blockThis = true;
                    }
                    foreach (ModuleEnginesFX engineModule in part.FindModulesImplementing<ModuleEnginesFX>())
                    {
                        if (engineModule.GetCurrentThrust() > 0) blockThis = true;
                    }

                    // Check for resource-consumption:
                    foreach (PartResource resource in part.Resources)
                    {
                        PartResourceDefinition resourceDefinition = null;
                        string resourceId = resource.resourceName.ToString();
                        if (!KSTS.resourceDictionary.TryGetValue(resourceId, out resourceDefinition)) continue;
                        if (resourceDefinition.density <= 0) continue; // We only care about resources with mass, skipping electricity and such.

                        if (!FlightRecorder.timerPartResources.ContainsKey(partId)) FlightRecorder.timerPartResources.Add(partId, new Dictionary<string, double>());
                        double lastAmount;
                        if (!FlightRecorder.timerPartResources[partId].TryGetValue(resourceId, out lastAmount))
                        {
                            FlightRecorder.timerPartResources[partId].Add(resourceId, resource.amount);
                        }
                        else
                        {
                            if (lastAmount != resource.amount) blockThis = true; // The amount has changed relative to the last timer-tick.
                        }
                    }

                    if (blockThis)
                    {
                        Debug.Log("[KSTS] marking part " + part.name.ToString() + " (" + part.flightID.ToString() + ") as used");
                        recording.usedPartIds.Add(part.flightID.ToString());
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[KSTS] FlightRecoorder.Timer(): " + e.ToString());
            }
        }
    }
}
