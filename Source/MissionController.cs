using System;
using System.Collections.Generic;
using UnityEngine;
using System.Text.RegularExpressions;
using System.IO;

namespace KSTS
{
    // Actual running mission:
    public enum MissionType { DEPLOY=1, TRANSPORT=2, CONSTRUCT=3 };
    public class Mission : Saveable
    {
        public MissionType missionType;
        public Orbit orbit = null;      // The orbit in which the new vesselo should get launched
        public string shipName = "";    // Name of the new vessel
        public double eta;              // Timestamp when this mission should end (checked in the timer-function).

        // We store the names of the profile and the template-file instead of their objects (as they are passed by the factory-method),
        // to make saving and loading these objects simpler. If we really need these objects, we can always look them up again.
        public string profileName = "";             // Name of the mission-profile (also its key)
        public string shipTemplateFilename = "";    // File of the saved ship's template

        public Guid? targetVesselId = null;        // The vessel referenced by a transport- or construction-mission
        public List<string> crewToDeliver = null;  // Names of kerbals to transport to the target-vessel
        public List<string> crewToCollect = null;  // Names of kerbals to bring back from the target-vessel
        public Dictionary<string, double> resourcesToDeliver = null; // ResourceName => ResourceAmount
        public string flagURL = null;              // The flag to be used for newly created vessels

        public MissionProfile GetProfile()
        {
            if (MissionController.missionProfiles.ContainsKey(profileName)) return MissionController.missionProfiles[profileName];
            return null;
        }

        public ShipTemplate GetShipTemplate()
        {
            return GUI.shipTemplates.Find(x => SanitizePath(x.template.filename) == shipTemplateFilename)?.template;
        }

        public static string GetMissionTypeName(MissionType type)
        {
            if (type == MissionType.DEPLOY) return "deployment";
            if (type == MissionType.TRANSPORT) return "transport";
            if (type == MissionType.CONSTRUCT) return "construction";
            return "N/A";
        }

        // Helper function for re-formating paths (like from vessel-templates) for save storage in config-nodes:
        public static string SanitizePath(string path)
        {
            path = Regex.Replace(path, @"\\", "/"); // Only fools use backslashes in paths
            path = Regex.Replace(path, @"(/+|/\\./)", "/"); // Remove redundant elements
            return path;
        }

        public static Mission CreateDeployment(string shipName, ShipTemplate template, Orbit orbit, MissionProfile profile, List<string> crew, string flagURL)
        {
            Mission mission = new Mission();
            mission.missionType = MissionType.DEPLOY;
            mission.shipTemplateFilename = SanitizePath(template.filename); // The filename contains silly portions like "KSP_x64_Data/..//saves", which break savegames because "//" starts a comment in the savegame ...
            mission.orbit = orbit;
            mission.shipName = shipName;
            mission.profileName = profile.profileName;
            mission.eta = Planetarium.GetUniversalTime() + profile.missionDuration;
            mission.crewToDeliver = crew; // The crew we want the new vessel to start with.
            mission.flagURL = flagURL;

            return mission;
        }

        public static Mission CreateTransport(Vessel target, MissionProfile profile, List<PayloadResource> resources, List<CrewTransferOrder> crewTransfers)
        {
            Mission mission = new Mission();
            mission.missionType = MissionType.TRANSPORT;
            mission.profileName = profile.profileName;
            mission.eta = Planetarium.GetUniversalTime() + profile.missionDuration;

            mission.targetVesselId = target.protoVessel.vesselID;
            if (resources != null)
            {
                mission.resourcesToDeliver = new Dictionary<string, double>();
                foreach (PayloadResource resource in resources)
                {
                    if (resource.amount > 0) mission.resourcesToDeliver.Add(resource.name, resource.amount);
                }
            }
            if (crewTransfers != null)
            {
                foreach (CrewTransferOrder crewTransfer in crewTransfers)
                {
                    switch (crewTransfer.direction)
                    {
                        case CrewTransferOrder.CrewTransferDirection.DELIVER:
                            if (mission.crewToDeliver == null) mission.crewToDeliver = new List<string>();
                            mission.crewToDeliver.Add(crewTransfer.kerbalName);
                            break;
                        case CrewTransferOrder.CrewTransferDirection.COLLECT:
                            if (mission.crewToCollect == null) mission.crewToCollect = new List<string>();
                            mission.crewToCollect.Add(crewTransfer.kerbalName);
                            break;
                        default:
                            throw new Exception("unknown transfer-direction: '"+ crewTransfer.direction.ToString() + "'");
                    }
                }
            }

            return mission;
        }

        public static Mission CreateConstruction(string shipName, ShipTemplate template, Vessel spaceDock, MissionProfile profile, List<string> crew, string flagURL, double constructionTime)
        {
            Mission mission = new Mission();
            mission.missionType = MissionType.CONSTRUCT;
            mission.shipTemplateFilename = SanitizePath(template.filename);
            mission.targetVesselId = spaceDock.protoVessel.vesselID;
            mission.shipName = shipName;
            mission.profileName = profile.profileName;
            mission.eta = Planetarium.GetUniversalTime() + constructionTime;
            mission.crewToDeliver = crew; // The crew we want the new vessel to start with.
            mission.flagURL = flagURL;

            return mission;
        }

        public static Mission CreateFromConfigNode(ConfigNode node)
        {
            Mission mission = new Mission();
            return (Mission)CreateFromConfigNode(node, mission);
        }

        // Tries to execute this mission and returns true if it was successfull:
        public bool TryExecute()
        {
            switch (missionType)
            {
                case MissionType.DEPLOY:
                    // Ship-Creation is only possible while not in flight with the current implementation:
                    if (HighLogic.LoadedScene != GameScenes.FLIGHT)
                    {
                        CreateShip();
                        return true;
                    }
                    return false;

                case MissionType.CONSTRUCT:
                    if (HighLogic.LoadedScene != GameScenes.FLIGHT)
                    {
                        Vessel targetVessel = null;
                        if (targetVesselId == null || (targetVessel = TargetVessel.GetVesselById((Guid)targetVesselId)) == null || !TargetVessel.IsValidTarget(targetVessel, MissionController.missionProfiles[profileName]))
                        {
                            // Abort mission (maybe the vessel was removed or got moved out of range):
                            Debug.Log("[KSTS] aborting transport-construction: target-vessel missing or out of range");
                            ScreenMessages.PostScreenMessage("Aborting construction-mission: Target-vessel not found at expected rendezvous-coordinates!");
                        }
                        else
                        {
                            CreateShip();
                            return true;
                        }
                    }
                    return false;

                case MissionType.TRANSPORT:
                    // Our functions for manipulating ships don't work on active vessels, beeing in flight however should be fine:
                    if (FlightGlobals.ActiveVessel == null || FlightGlobals.ActiveVessel.id != targetVesselId)
                    {
                        Vessel targetVessel = null;
                        if (!MissionController.missionProfiles.ContainsKey(profileName)) throw new Exception("unable to execute transport-mission, profile '"+ profileName + "' missing");
                        if (targetVesselId == null || (targetVessel = TargetVessel.GetVesselById((Guid)targetVesselId)) == null || !TargetVessel.IsValidTarget(targetVessel, MissionController.missionProfiles[profileName]))
                        {
                            // Abort mission (maybe the vessel was removed or got moved out of range):
                            Debug.Log("[KSTS] aborting transport-mission: target-vessel missing or out of range");
                            ScreenMessages.PostScreenMessage("Aborting transport-mission: Target-vessel not found at expected rendezvous-coordinates!");
                        }
                        else
                        {
                            // Do the actual transport-mission:
                            if (resourcesToDeliver != null)
                            {
                                foreach (KeyValuePair<string, double> item in resourcesToDeliver) TargetVessel.AddResources(targetVessel, item.Key, item.Value);
                            }
                            if (crewToCollect != null)
                            {
                                foreach (string kerbonautName in crewToCollect) TargetVessel.RecoverCrewMember(targetVessel, kerbonautName);
                            }
                            if (crewToDeliver != null)
                            {
                                foreach (string kerbonautName in crewToDeliver) TargetVessel.AddCrewMember(targetVessel, kerbonautName);
                            }
                        }
                        return true;
                    }
                    return false;

                default:
                    throw new Exception("unexpected mission-type '" + missionType.ToString() + "'");
            }
            return false;
        }

        // Helper function for building an ordered list of parts which are attached to the given root-part. The resulting
        // List is passed by reference and also returend.
        private List<Part> FindAndAddAttachedParts(Part p,ref List<Part> list)
        {
            if (list == null) list = new List<Part>();
            if (list.Contains(p)) return list;
            list.Add(p);
            foreach (AttachNode an in p.attachNodes)
            {
                if (an.attachedPart == null || list.Contains(an.attachedPart)) continue;
                FindAndAddAttachedParts(an.attachedPart,ref list);
            }
            return list;
        }

        // Creates a new ship with the given parameters for this mission. The code however seems unnecessarily convoluted and
        // error-prone, but there are no better examples available on the internet.
        private void CreateShip()
        {
            // TODO: Apply the staging which was saved in the editor.
            // TODO: Settings from other mods like Part-Switchers must also be applied here, maybe copy the config-nodes...
            try
            {
                if (!File.Exists(shipTemplateFilename)) throw new Exception("file '" + shipTemplateFilename + "' not found");
                if (missionType == MissionType.DEPLOY)
                {
                    // Make sure that there won't be any collisions, when the vessel is created at the given orbit:
                    orbit = GUIOrbitEditor.ApplySafetyDistance(orbit);
                }
                else if (missionType == MissionType.CONSTRUCT)
                {
                    // Deploy the new ship next to the space-dock:
                    Vessel spaceDock = TargetVessel.GetVesselById((Guid)targetVesselId);
                    orbit = GUIOrbitEditor.CreateFollowingOrbit(spaceDock.orbit, 100); // TODO: Calculate the distance using the ships's size
                    orbit = GUIOrbitEditor.ApplySafetyDistance(orbit);
                }
                else throw new Exception("invalid mission-type '"+missionType.ToString()+"'");

                // The ShipConstruct-object can only savely exist while not in flight, otherwise it will spam Null-Pointer Exceptions every tick:
                if (HighLogic.LoadedScene == GameScenes.FLIGHT) throw new Exception("unable to run CreateShip while in flight");

                // Load the parts form the saved vessel:
                ShipConstruct shipConstruct = ShipConstruction.LoadShip(shipTemplateFilename);
                ProtoVessel dummyProto = new ProtoVessel(new ConfigNode(), null);
                Vessel dummyVessel = new Vessel();
                dummyProto.vesselRef = dummyVessel;

                // In theory it should be enough to simply copy the parts from the ShipConstruct to the ProtoVessel, but
                // this only seems to work when the saved vessel starts with the root-part and is designed top down from there.
                // It seems that the root part has to be the first part in the ProtoVessel's parts-list and all other parts have
                // to be listed in sequence radiating from the root part (eg 1=>2=>R<=3<=4 should be R,2,1,3,4). If the parts
                // are not in the correct order, their rotation seems to get messed up or they are attached to the wrong
                // attachmet-nodes, which is why we have to re-sort the parts with our own logic here.
                // This part of the code is experimental however and only based on my own theories and observations about KSP's vessels.
                Part rootPart = null;
                foreach (Part p in shipConstruct.parts)
                {
                    if (p.parent == null) { rootPart = p; break; }
                }
                List<Part> pList = null;
                dummyVessel.parts = FindAndAddAttachedParts(rootPart, ref pList); // Find all parts which are directly attached to the root-part and add them in order.

                // Handle Subassemblies which are attached by surface attachment-nodes:
                bool handleSurfaceAttachments = true;
                while (dummyVessel.parts.Count < shipConstruct.parts.Count)
                {
                    int processedParts = 0;
                    foreach (Part p in shipConstruct.parts)
                    {
                        if (dummyVessel.parts.Contains(p)) continue;
                        if (handleSurfaceAttachments)
                        {
                            // Check if the part is attached by a surface-node:
                            if (p.srfAttachNode != null && dummyVessel.parts.Contains(p.srfAttachNode.attachedPart))
                            {
                                // Add this surface attached part and all the sub-parts:
                                dummyVessel.parts = FindAndAddAttachedParts(p, ref dummyVessel.parts);
                                processedParts++;
                            }
                        }
                        else
                        {
                            // Simply copy this part:
                            dummyVessel.parts.Add(p);
                        }
                    }
                    if (processedParts == 0)
                    {
                        // If there are still unprocessed parts, just throw them in the list during the next iteration,
                        // this should not happen but we don't want to end up in an endless loop:
                        handleSurfaceAttachments = false;
                    }
                }

                // Initialize all parts:
                uint missionID = (uint)Guid.NewGuid().GetHashCode();
                uint launchID = HighLogic.CurrentGame.launchID++;
                foreach (Part p in dummyVessel.parts)
                {
                    p.flagURL = flagURL == null ? HighLogic.CurrentGame.flagURL : flagURL;
                    p.missionID = missionID;
                    p.launchID = launchID;
                    p.temperature = 1.0;

                    // If the KRnD-Mod is installed, make sure that all parts of this newly created ship are set to the lates version:
                    foreach (PartModule module in p.Modules)
                    {
                        if (module.moduleName != "KRnDModule") continue;
                        Debug.Log("[KSTS] found KRnD on '" + p.name.ToString() + "', setting to latest stats");
                        foreach (BaseField field in module.Fields)
                        {
                            if (field.name.ToString() == "upgradeToLatest")
                            {
                                field.SetValue(1, module); // Newer versions of KRnD use this flag to upgrade all attributes of the given part to the latest levels, when the vessel is activated.
                                if (field.GetValue(module).ToString() != "1") Debug.LogError("[KSTS] unable to modify '" + field.name.ToString() + "'");
                            }
                        }
                    }

                    dummyProto.protoPartSnapshots.Add(new ProtoPartSnapshot(p, dummyProto));
                }

                // Store the parts in Config-Nodes:
                foreach (ProtoPartSnapshot p in dummyProto.protoPartSnapshots)
                {
                    p.storePartRefs();
                }
                List<ConfigNode> partNodesL = new List<ConfigNode>();
                foreach (var snapShot in dummyProto.protoPartSnapshots)
                {
                    ConfigNode node = new ConfigNode("PART");
                    snapShot.Save(node);
                    partNodesL.Add(node);
                }
                ConfigNode[] partNodes = partNodesL.ToArray();
                ConfigNode[] additionalNodes = new ConfigNode[0];

                // This will actually create the ship and add it to the global list of flights:
                ConfigNode protoVesselNode = ProtoVessel.CreateVesselNode(shipName, VesselType.Ship, orbit, 0, partNodes, additionalNodes);
                ProtoVessel pv = HighLogic.CurrentGame.AddVessel(protoVesselNode);
                Debug.Log("[KSTS] deployed new ship '" + shipName.ToString() + "' as '" + pv.vesselRef.id.ToString() + "'");
                ScreenMessages.PostScreenMessage("Vessel '" + shipName.ToString() + "' deployed"); // Popup message to notify the player
                Vessel newVessel = FlightGlobals.Vessels.Find(x => x.id == pv.vesselID);

                // Maybe add the initial crew to the vessel:
                if (crewToDeliver != null && crewToDeliver.Count > 0 && newVessel != null)
                {
                    foreach (string kerbonautName in crewToDeliver) TargetVessel.AddCrewMember(newVessel, kerbonautName);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[KSTS] Mission.CreateShip(): " + e.ToString());
            }
        }

        // Generates a description for displaying on the GUI:
        public string GetDescription()
        {
            string description = "<color=#F9FA86><b>" + profileName + "</b></color> <color=#FFFFFF>(" + GetMissionTypeName(missionType) + ")\n";

            ShipTemplate shipTemplate = GetShipTemplate();
            if (shipTemplate != null) description += "<b>Ship:</b> " + shipName + " (" + shipTemplate.shipName.ToString() + ")\n";
            if (orbit != null) description += "<b>Orbit:</b> " + orbit.referenceBody.bodyName.ToString() + " @ " + GUI.FormatAltitude(orbit.semiMajorAxis - orbit.referenceBody.Radius) + "\n";

            // Display the targeted vessel (transport- and construction-missions):
            Vessel targetVessel = null;
            if (targetVesselId != null && (targetVessel = TargetVessel.GetVesselById((Guid)targetVesselId)) != null)
            {
                description += "<b>Target:</b> " + targetVessel.vesselName + " @ " + GUI.FormatAltitude(targetVessel.altitude) + "\n";
            }

            // Display the total weight of the payload we are hauling (transport-missions):
            if (resourcesToDeliver != null)
            {
                double totalMass = 0;
                foreach (KeyValuePair<string, double> item in resourcesToDeliver)
                {
                    if (!KSTS.resourceDictionary.ContainsKey(item.Key)) continue;
                    totalMass += KSTS.resourceDictionary[item.Key].density * item.Value;
                }
                description += "<b>Cargo:</b> " + totalMass.ToString("#,##0.00t") + "\n";
            }

            // Display the crew-members we are transporting and collection:
            if (crewToDeliver != null)
            {
                description += "<b>Crew-Transfer (Outbound):</b> " + String.Join(", ", crewToDeliver.ToArray()).Replace(" Kerman","") + "\n";
            }
            if (crewToCollect != null)
            {
                description += "<b>Crew-Transfer (Inbound):</b> " + String.Join(", ", crewToCollect.ToArray()).Replace(" Kerman", "") + "\n";
            }

            // Display the remaining time:
            double remainingTime = eta - Planetarium.GetUniversalTime();
            if (remainingTime < 0) remainingTime = 0;
            int etaColorComponent = 0xFF;
            if (remainingTime <= 300) etaColorComponent = (int)Math.Round((0xFF / 300.0) * remainingTime); // Starting at 5 minutes, start turning the ETA green.
            string etaColor = "#" + etaColorComponent.ToString("X2") + "FF" + etaColorComponent.ToString("X2");
            description += "<color="+etaColor+"><b>ETA:</b> " + GUI.FormatDuration(remainingTime)+"</color>";

            description += "</color>";
            return description;
        }
    }

    // Recorded mission-profile for a flight:
    public enum MissionProfileType { DEPLOY=1, TRANSPORT=2 };
    public class MissionProfile : Saveable
    {
        public string profileName = "";
        public string vesselName = "";
        public MissionProfileType missionType;
        public double launchCost = 0;
        public double launchMass = 0;
        public double payloadMass = 0;
        public double minAltitude = 0;
        public double maxAltitude = 0;
        public string bodyName = "";
        public double missionDuration = 0;
        public bool oneWayMission = true;
        public int crewCapacity = 0;
        public List<string> dockingPortTypes = null;

        public static string GetMissionProfileTypeName(MissionProfileType type)
        {
            if (type == MissionProfileType.DEPLOY) return "deployment";
            if (type == MissionProfileType.TRANSPORT) return "transport";
            return "N/A";
        }

        public static MissionProfile CreateFromConfigNode(ConfigNode node)
        {
            MissionProfile missionProfile = new MissionProfile();
            return (MissionProfile)CreateFromConfigNode(node, missionProfile);
        }

        public static MissionProfile CreateFromRecording(Vessel vessel, FlightRecording recording)
        {
            MissionProfile profile = new MissionProfile();
            
            profile.profileName     = recording.profileName;
            profile.vesselName      = vessel.vesselName.ToString();
            profile.missionType     = recording.missionType;
            profile.launchCost      = recording.launchCost;
            profile.launchMass      = recording.launchMass - recording.payloadMass;
            profile.payloadMass     = recording.payloadMass;
            profile.minAltitude     = recording.minAltitude;
            profile.maxAltitude     = recording.maxAltitude;
            profile.bodyName        = recording.launchBodyName;
            profile.missionDuration = recording.deploymentTime - recording.startTime;
            profile.crewCapacity    = vessel.GetCrewCapacity() - vessel.GetCrewCount(); // Capacity at the end of the mission, so we can use it for oneway- as well als return-trips.
            profile.dockingPortTypes = recording.dockingPortTypes;

            if (vessel.situation == Vessel.Situations.LANDED || vessel.situation == Vessel.Situations.SPLASHED)
            {
                profile.oneWayMission = false;
                profile.launchCost -= recording.GetCurrentVesselValue();
                if (profile.launchCost < 0) profile.launchCost = 0; // Shouldn't happen
            }
            else profile.oneWayMission = true;

            return profile;
        }
    }

    class MissionController
    {
        public static Dictionary<string, MissionProfile> missionProfiles = null;
        public static List<Mission> missions = null;

        public static void Initialize()
        {
            if (MissionController.missionProfiles == null) MissionController.missionProfiles = new Dictionary<string, MissionProfile>();
            if (MissionController.missions == null) MissionController.missions = new List<Mission>();
        }

        private static string GetUniqueProfileName(string name)
        {
            name = name.Trim();
            if (name == "") name = "KSTS";

            string[] postfixes = { "Alpha", "Beta", "Delta", "Epsilon", "Zeta", "Eta", "Theta", "Iota", "Kappa", "Lambda", "Omega" };
            int postfixNumber = 0;
            string uniqueName = name;
            bool lowercase = name.ToLower() == name; // If the name is in all lowercase, we don't want to break it by adding uppercase letters
            while (MissionController.missionProfiles.ContainsKey(uniqueName))
            {
                uniqueName = name + " ";
                if (postfixNumber >= postfixes.Length) uniqueName += postfixNumber.ToString();
                else uniqueName += postfixes[postfixNumber];
                if (lowercase) uniqueName = uniqueName.ToLower();
                postfixNumber++;
            }
            return uniqueName;
        }

        public static void CreateMissionProfile(Vessel vessel, FlightRecording recording)
        {
            MissionProfile profile = MissionProfile.CreateFromRecording(vessel, recording);

            // Make the profile-name unique to use it as a key:
            profile.profileName = MissionController.GetUniqueProfileName(profile.profileName);

            MissionController.missionProfiles.Add(profile.profileName, profile);
            Debug.Log("[KSTS] saved new mission profile '" + profile.profileName + "'");
        }

        public static void DeleteMissionProfile(string name)
        {
            // Abort all running missions of this profile:
            int cancelledMission = missions.RemoveAll(x => x.profileName == name);
            if (cancelledMission > 0)
            {
                Debug.Log("[KSTS] cancelled " + cancelledMission.ToString() + " missions due to profile-deletion");
                ScreenMessages.PostScreenMessage("Cancelled " + cancelledMission.ToString() + " missions!");
            }

            // Remove the profile:
            if (MissionController.missionProfiles.ContainsKey(name)) MissionController.missionProfiles.Remove(name);
        }

        public static void ChangeMissionProfileName(string name, string newName)
        {
            MissionProfile profile = null;
            if (!MissionController.missionProfiles.TryGetValue(name, out profile)) return;
            MissionController.missionProfiles.Remove(name);
            profile.profileName = MissionController.GetUniqueProfileName(newName);
            MissionController.missionProfiles.Add(profile.profileName, profile);
        }

        public static void LoadMissions(ConfigNode node)
        {
            MissionController.missionProfiles.Clear();
            ConfigNode missionProfilesNode = node.GetNode("MissionProfiles");
            if (missionProfilesNode != null)
            {
                foreach (ConfigNode missionProfileNode in missionProfilesNode.GetNodes())
                {
                    MissionProfile missionProfile = MissionProfile.CreateFromConfigNode(missionProfileNode);
                    MissionController.missionProfiles.Add(missionProfile.profileName, missionProfile);
                }
            }

            MissionController.missions.Clear();
            ConfigNode missionsNode = node.GetNode("Missions");
            if (missionsNode != null)
            {
                foreach (ConfigNode missionNode in missionsNode.GetNodes())
                {
                    MissionController.missions.Add(Mission.CreateFromConfigNode(missionNode));
                }
            }
        }

        public static void SaveMissions(ConfigNode node)
        {
            ConfigNode missionProfilesNode = node.AddNode("MissionProfiles");
            foreach (KeyValuePair<string, MissionProfile> item in MissionController.missionProfiles)
            {
                missionProfilesNode.AddNode(item.Value.CreateConfigNode("MissionProfile"));
            }

            ConfigNode missionsNode = node.AddNode("Missions");
            foreach (Mission mission in MissionController.missions)
            {
                missionsNode.AddNode(mission.CreateConfigNode("Mission"));
            }
        }

        public static void StartMission(Mission mission)
        {
            MissionController.missions.Add(mission);
        }

        // Returns the mission (if any), the given kerbal is assigned to:
        public static Mission GetKerbonautsMission(string kerbonautName)
        {
            foreach (Mission mission in missions)
            {
                if (mission.crewToDeliver != null && mission.crewToDeliver.Contains(kerbonautName)) return mission;
                if (mission.crewToCollect != null && mission.crewToCollect.Contains(kerbonautName)) return mission;
            }
            return null;
        }

        // Is called every second and handles the running missions:
        public static void Timer()
        {
            try
            {
                double now = Planetarium.GetUniversalTime();
                List<Mission> toExecute = new List<Mission>();
                foreach (Mission mission in missions)
                {
                    if (mission.eta <= now) toExecute.Add(mission);
                }
                foreach (Mission mission in toExecute)
                {
                    try
                    {
                        if (mission.TryExecute())
                        {
                            missions.Remove(mission);
                        }
                    }
                    catch (Exception e)
                    {
                        // This is serious, but to avoid calling "execute" on every timer-tick, we better remove this mission:
                        Debug.LogError("[KSTS] FlightRecoorder.Timer().TryExecute(): " + e.ToString());
                        Debug.LogError("[KSTS] cancelling broken mission");
                        missions.Remove(mission);
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
