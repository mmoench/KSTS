using System.Collections.Generic;
using UnityEngine;

namespace KSTS
{
    class GUIRecordingTab
    {
        private static bool initialized = false;
        private static int selectedMissionTypeTab = 0;
        private static Vector2 scrollPos = Vector2.zero;
        private static Dictionary<string, double> selectedPayloadDeploymentResources = null;
        private static List<string> selectedPayloadAssemblyIds = null;
        private static GUIMissionProfileSelector missionProfileSelector = null;
        private static MissionProfile lastSelectedProfile = null;
        private static string newMissionProfileName = "";

        public static void Initialize()
        {
            if (selectedPayloadDeploymentResources == null) selectedPayloadDeploymentResources = new Dictionary<string, double>();
            if (selectedPayloadAssemblyIds == null) selectedPayloadAssemblyIds = new List<string>();
            if (missionProfileSelector == null) missionProfileSelector = new GUIMissionProfileSelector();
            initialized = true;
        }

        public static void Reset()
        {
            // It is not strictly necessary to clear these lists, but if someone would record hundreds of flights without pause,
            // they would keep growing:
            if (selectedPayloadDeploymentResources != null) selectedPayloadDeploymentResources.Clear();
            if (selectedPayloadAssemblyIds != null) selectedPayloadAssemblyIds.Clear();
        }

        public static void Display()
        {
            if (!initialized) Initialize();
            Vessel vessel = FlightGlobals.ActiveVessel;
            FlightRecording recording = null;
            if (vessel) recording = FlightRecoorder.GetFlightRecording(vessel);
            if (!vessel || recording == null)
            {
                Reset();

                // Show list of recorded profiles:
                missionProfileSelector.DisplayList();
                if (missionProfileSelector.selectedProfile != null)
                {
                    if (missionProfileSelector.selectedProfile != lastSelectedProfile)
                    {
                        // The selecte profile was switched:
                        lastSelectedProfile = missionProfileSelector.selectedProfile;
                        newMissionProfileName = missionProfileSelector.selectedProfile.profileName;
                    }

                    GUILayout.BeginHorizontal();
                    GUILayout.Label("<size=14><b>Profile name:</b></size>", new GUIStyle(GUI.labelStyle) { stretchWidth = false });
                    newMissionProfileName = GUILayout.TextField(newMissionProfileName, new GUIStyle(GUI.textFieldStyle) { stretchWidth = true });
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Save Profile", GUI.buttonStyle))
                    {
                        MissionController.ChangeMissionProfileName(missionProfileSelector.selectedProfile.profileName, newMissionProfileName);
                        missionProfileSelector = new GUIMissionProfileSelector(); // Deselect & Reset
                    }
                    if (GUILayout.Button("Delete Profile", GUI.buttonStyle))
                    {
                        MissionController.DeleteMissionProfile(missionProfileSelector.selectedProfile.profileName);
                        missionProfileSelector = new GUIMissionProfileSelector(); // Deselect & Reset
                    }
                    GUILayout.EndHorizontal();
                }
            }
            else
            {
                // During the recording, allow the player to change the name of the new flight-profile:
                GUILayout.BeginHorizontal();
                GUILayout.Label("<size=14><b>Profile name:</b></size>", new GUIStyle(GUI.labelStyle) { stretchWidth = false });
                if (recording.status != FlightRecordingStatus.PRELAUNCH)
                {
                    recording.profileName = GUILayout.TextField(recording.profileName, new GUIStyle(GUI.textFieldStyle) { stretchWidth = true });
                }
                else
                {
                    GUILayout.Label(recording.profileName, new GUIStyle(GUI.labelStyle) { stretchWidth = true });
                }
                GUILayout.EndHorizontal();

                // Display all Information about the current recording:
                GUILayout.BeginScrollView(new Vector2(0, 0), new GUIStyle(GUI.scrollStyle) { stretchHeight = true });
                List<KeyValuePair<string, string>> displayAttributes = recording.GetDisplayAttributes();
                foreach (KeyValuePair<string, string> displayAttribute in displayAttributes)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("<b>" + displayAttribute.Key + "</b>");
                    GUILayout.Label(displayAttribute.Value + "  ", new GUIStyle(GUI.labelStyle) { alignment = TextAnchor.MiddleRight });
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndScrollView();

                // Display payload selector:
                if (recording.status == FlightRecordingStatus.ASCENDING || recording.status == FlightRecordingStatus.PRELAUNCH)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("<size=14><b>Mission Type:</b></size>");
                    string[] missionTypeStrings = new string[] { "Deploy", "Transport" };
                    selectedMissionTypeTab = GUILayout.Toolbar(selectedMissionTypeTab, missionTypeStrings);
                    GUILayout.EndHorizontal();

                    scrollPos = GUILayout.BeginScrollView(scrollPos, GUI.scrollStyle, GUILayout.Height(210), GUILayout.MaxHeight(210));
                    if (selectedMissionTypeTab == 0)
                    {
                        // Show all deployable payloads:
                        if (!recording.CanPerformMission(MissionProfileType.DEPLOY))
                        {
                            GUILayout.Label("Deployment missions can only be performed if the vessel has detachable parts which haven't been used during the flight (no resource consumption, inactive, uncrewed, etc).");
                        }
                        else
                        {
                            // Show all detachable subassemblies:
                            bool selectionChanged = false;
                            foreach (PayloadAssembly payloadAssembly in recording.GetPayloadAssemblies())
                            {
                                GUILayout.BeginHorizontal();
                                if (GUILayout.Toggle(selectedPayloadAssemblyIds.Contains(payloadAssembly.id), "<b>" + payloadAssembly.name + "</b>"))
                                {
                                    if (!selectedPayloadAssemblyIds.Contains(payloadAssembly.id))
                                    {
                                        selectedPayloadAssemblyIds.Add(payloadAssembly.id);
                                        selectionChanged = true;
                                    }
                                }
                                else
                                {
                                    if (selectedPayloadAssemblyIds.Contains(payloadAssembly.id))
                                    {
                                        selectedPayloadAssemblyIds.Remove(payloadAssembly.id);
                                        selectionChanged = true;
                                    }
                                }
                                GUILayout.Label(payloadAssembly.partCount.ToString() + " part" + (payloadAssembly.partCount != 1 ? "s" : "") + ", " + payloadAssembly.mass.ToString("#,##0.00 t") + "   ", new GUIStyle(GUI.labelStyle) { alignment = TextAnchor.MiddleRight });
                                GUILayout.EndHorizontal();
                            }

                            // Highlight all selected assemblies (to make sure these don't cancel each other out, we first turn all off and then switch the selected ones on):
                            if (selectionChanged)
                            {
                                foreach (PayloadAssembly payloadAssembly in recording.GetPayloadAssemblies()) payloadAssembly.Highlight(false);
                                foreach (PayloadAssembly payloadAssembly in recording.GetPayloadAssemblies()) if (selectedPayloadAssemblyIds.Contains(payloadAssembly.id)) payloadAssembly.Highlight(true);
                            }
                        }
                    }
                    else
                    {
                        if (!recording.CanPerformMission(MissionProfileType.TRANSPORT))
                        {
                            GUILayout.Label("Transport missions can only be performed with vessels which have at least one docking port as well as RCS thrusters.");
                        }
                        else
                        {
                            // Show all payload-resources:
                            double totalPayloadMass = 0;
                            foreach (PayloadResource payloadResource in recording.GetPayloadResources())
                            {
                                double selectedAmount = 0;
                                selectedPayloadDeploymentResources.TryGetValue(payloadResource.name, out selectedAmount);

                                GUILayout.BeginHorizontal();
                                GUILayout.Label("<b>" + payloadResource.name + "</b>");
                                GUILayout.Label(((selectedAmount / payloadResource.amount) * 100).ToString("0.00") + "% (" + selectedAmount.ToString("#,##0.00") + " / " + payloadResource.amount.ToString("#,##0.00") + "): " + (selectedAmount * payloadResource.mass).ToString("#,##0.00 t") + "  ", new GUIStyle(GUI.labelStyle) { alignment = TextAnchor.MiddleRight });
                                GUILayout.EndHorizontal();

                                selectedAmount = GUILayout.HorizontalSlider((float)selectedAmount, 0, (float)payloadResource.amount);
                                if (selectedAmount < 0) selectedAmount = 0;
                                if (selectedAmount > payloadResource.amount) selectedAmount = payloadResource.amount;
                                if (payloadResource.amount - selectedAmount < 0.01) selectedAmount = payloadResource.amount;
                                totalPayloadMass += selectedAmount * payloadResource.mass;

                                if (selectedPayloadDeploymentResources.ContainsKey(payloadResource.name)) selectedPayloadDeploymentResources[payloadResource.name] = selectedAmount;
                                else selectedPayloadDeploymentResources.Add(payloadResource.name, selectedAmount);
                            }

                            GUILayout.BeginHorizontal();
                            GUILayout.Label("<b>Total Payload</b>");
                            GUILayout.Label(totalPayloadMass.ToString("#,##0.00 t  "), new GUIStyle(GUI.labelStyle) { alignment = TextAnchor.MiddleRight });
                            GUILayout.EndHorizontal();
                        }
                    }
                    GUILayout.EndScrollView();
                }

                // Bottom pane with action-buttons:
                GUILayout.BeginHorizontal();
                if (recording.status == FlightRecordingStatus.PRELAUNCH && GUILayout.Button("Record", GUI.buttonStyle))
                {
                    // Start Recording:
                    FlightRecoorder.StartRecording(vessel);
                }

                if (recording.CanDeploy() && GUILayout.Button("Release Payload", GUI.buttonStyle))
                {
                    if (selectedMissionTypeTab == 0)
                    {
                        List<PayloadAssembly> payloadAssemblies = recording.GetPayloadAssemblies();
                        List<PayloadAssembly> selectedPayloadAssemblies = new List<PayloadAssembly>();
                        foreach (PayloadAssembly payloadAssembly in recording.GetPayloadAssemblies())
                        {
                            if (selectedPayloadAssemblyIds.Contains(payloadAssembly.id)) selectedPayloadAssemblies.Add(payloadAssembly);
                        }
                        if (selectedPayloadAssemblies.Count > 0) recording.DeployPayloadAssembly(selectedPayloadAssemblies);
                    }
                    else
                    {
                        recording.DeployPayloadResources(selectedPayloadDeploymentResources);
                    }
                }

                if (recording.CanFinish() && GUILayout.Button("Stop & Save", GUI.buttonStyle))
                {
                    // Stop recording and create a mission-profile:
                    FlightRecoorder.SaveRecording(vessel);
                }

                if (recording.status != FlightRecordingStatus.PRELAUNCH && GUILayout.Button("Abort", GUI.buttonStyle))
                {
                    // Cancel runnig recording:
                    FlightRecoorder.CancelRecording(vessel);
                }
                GUILayout.EndHorizontal();
            }
        }
    }
}
