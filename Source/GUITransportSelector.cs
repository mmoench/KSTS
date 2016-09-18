using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace KSTS
{
    public class CrewTransferOrder
    {
        public enum CrewTransferDirection { DELIVER = 1, COLLECT = 2 };
        public string kerbalName;
        public CrewTransferDirection direction;
    }

    public class GUITransportSelector
    {
        public List<PayloadResource> selectedResources = null;
        public List<CrewTransferOrder> selectedCrewTransfers = null;

        private Vector2 scrollPos = Vector2.zero;
        private List<GUIRichValueSelector> resourceSelectors = null;
        private List<PayloadResource> availableResources = null;
        private MissionProfile missionProfile = null;
        private Vessel targetVessel = null;
        private int selectedTransportType = 0;
        private List<string> crewToDeliver = null;
        private List<string> crewToCollect = null;

        public GUITransportSelector(Vessel targetVessel, MissionProfile missionProfile)
        {
            this.targetVessel = targetVessel;
            this.missionProfile = missionProfile;
            resourceSelectors = new List<GUIRichValueSelector>();
            availableResources = TargetVessel.GetFreeResourcesCapacities(targetVessel);
            foreach (PayloadResource availablePayload in availableResources)
            {
                GUIRichValueSelector selector = new GUIRichValueSelector(availablePayload.name, 0, "", 0, Math.Round(availablePayload.amount,2), true, "#,##0.00");
                resourceSelectors.Add(selector);
            }
            crewToDeliver = new List<string>();
            crewToCollect = new List<string>();
        }

        // Shows a list of all available payload-resources the player can choose from:
        public bool DisplayList()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("<size=14><b>Cargo:</b></size>");
            string[] transportTypeStrings = new string[] { "Resources", "Crew" };
            selectedTransportType = GUILayout.Toolbar(selectedTransportType, transportTypeStrings);
            GUILayout.EndHorizontal();

            scrollPos = GUILayout.BeginScrollView(scrollPos, GUI.scrollStyle);
            if (selectedTransportType == 0)
            {
                // Transport Resources:
                selectedCrewTransfers = null;
                if (resourceSelectors.Count == 0)
                {
                    GUILayout.Label("The selected target has no free capacity to receive resources.");
                }
                else
                {
                    // Show list with all possible payloads:
                    int index = 0;
                    double selectedMass = 0;
                    List<PayloadResource> currentlySelectedPayloads = new List<PayloadResource>();
                    foreach (GUIRichValueSelector selector in resourceSelectors)
                    {
                        selector.Display();
                        PayloadResource resource = availableResources[index];
                        PayloadResource selected = resource.Clone();
                        selected.amount = selector.Value;
                        currentlySelectedPayloads.Add(selected);
                        selectedMass += selected.amount * selected.mass;
                        index++;
                    }

                    // Show total selected amount:
                    string textColor = "#00FF00";
                    if (selectedMass > missionProfile.payloadMass) textColor = "#FF0000";
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("<b>Selected Payload:</b>");
                    GUILayout.Label("<color=" + textColor + ">" + selectedMass.ToString("#,##0.00 t") + " / " + missionProfile.payloadMass.ToString("#,##0.00 t") + "</color>  ", new GUIStyle(GUI.labelStyle) { alignment = TextAnchor.MiddleRight });
                    GUILayout.EndHorizontal();

                    // If the selected mass falls in the range of the transport capacity, we can use the current selection for the mission:
                    if (selectedMass > 0 && selectedMass <= missionProfile.payloadMass) selectedResources = currentlySelectedPayloads;
                    else selectedResources = null;
                }
            }
            else
            {
                // Transport Crew:
                selectedResources = null;
                int targetCrewCapacity = TargetVessel.GetCrewCapacity(targetVessel);
                if (missionProfile.crewCapacity == 0)
                {
                    GUILayout.Label("There are no available seats in the selected mission-profile.");
                }
                else if (targetCrewCapacity == 0)
                {
                    GUILayout.Label("The selected target-vessel can not hold any crew-members.");
                }
                else
                {
                    // Target-vessel summary:
                    bool targetOverload = false;
                    if (targetVessel.GetVesselCrew().Count + crewToDeliver.Count - crewToCollect.Count > targetCrewCapacity) targetOverload = true;
                    string headline = "<b>" + targetVessel.vesselName + ":</b> " + targetVessel.GetVesselCrew().Count.ToString() + "/" + targetCrewCapacity.ToString();
                    string transfers = " inbound: " + crewToDeliver.Count.ToString("+#;-#;0") + ", outbound: " + (-crewToCollect.Count).ToString("+#;-#;0");
                    if (targetOverload) transfers = "<color=#FF0000>" + transfers + "</color>";
                    GUILayout.Label(headline + transfers);

                    // Display Crew that is stationed on the target vessel:
                    foreach (ProtoCrewMember kerbonaut in targetVessel.GetVesselCrew())
                    {
                        string details = " <b>" + kerbonaut.name + "</b> (Level " + kerbonaut.experienceLevel.ToString() + " " + kerbonaut.trait + ")";
                        if (missionProfile.oneWayMission || MissionController.GetKerbonautsMission(kerbonaut.name) != null) GUILayout.Label(" • " + details); // Do not transport kerbals, which are flagged for another mission or there isn't even a return-trip
                        else
                        {
                            bool selected = GUILayout.Toggle(crewToCollect.Contains(kerbonaut.name), details);
                            if (selected && !crewToCollect.Contains(kerbonaut.name)) crewToCollect.Add(kerbonaut.name);
                            else if (!selected && crewToCollect.Contains(kerbonaut.name)) crewToCollect.Remove(kerbonaut.name);
                        }
                    }

                    GUILayout.Label("");

                    // Transport-vessel summary:
                    bool transportOutboundOverload = false;
                    if (crewToDeliver.Count > missionProfile.crewCapacity) transportOutboundOverload = true;
                    bool transportInboundOverload = false;
                    if (crewToCollect.Count > missionProfile.crewCapacity) transportInboundOverload = true;

                    headline = "<b>" + missionProfile.vesselName + ":</b> ";
                    string outbound = "outbound: " + crewToDeliver.Count.ToString() + "/" + missionProfile.crewCapacity.ToString();
                    if (transportOutboundOverload) outbound = "<color=#FF0000>" + outbound + "</color>";
                    string inbound = "";
                    if (!missionProfile.oneWayMission)
                    {
                        inbound = ", inbound: " + crewToCollect.Count.ToString() + "/" + missionProfile.crewCapacity.ToString();
                        if (transportInboundOverload) inbound = "<color=#FF0000>" + inbound + "</color>";
                    }
                    else inbound += ", inbound: -";
                    GUILayout.Label(headline + outbound + inbound);

                    // Display crew-rowster:
                    foreach (ProtoCrewMember kerbonaut in HighLogic.CurrentGame.CrewRoster.Kerbals(ProtoCrewMember.KerbalType.Crew, ProtoCrewMember.RosterStatus.Available))
                    {
                        string details = " <b>" + kerbonaut.name + "</b> (Level " + kerbonaut.experienceLevel.ToString() + " " + kerbonaut.trait.ToString() + ")";
                        if (MissionController.GetKerbonautsMission(kerbonaut.name) != null) GUILayout.Label(" • " + details); // Do not transport kerbals, which are flagged for another mission
                        else
                        {
                            bool selected = GUILayout.Toggle(crewToDeliver.Contains(kerbonaut.name), details);
                            if (selected && !crewToDeliver.Contains(kerbonaut.name)) crewToDeliver.Add(kerbonaut.name);
                            else if (!selected && crewToDeliver.Contains(kerbonaut.name)) crewToDeliver.Remove(kerbonaut.name);
                        }
                    }

                    // If there is a valid selection, which neither overloads the target nor the transport, copy the selection:
                    if (!targetOverload && !transportOutboundOverload && !transportInboundOverload)
                    {
                        selectedCrewTransfers = new List<CrewTransferOrder>();
                        foreach (string name in crewToDeliver) selectedCrewTransfers.Add(new CrewTransferOrder() { kerbalName = name, direction = CrewTransferOrder.CrewTransferDirection.DELIVER });
                        foreach (string name in crewToCollect) selectedCrewTransfers.Add(new CrewTransferOrder() { kerbalName = name, direction = CrewTransferOrder.CrewTransferDirection.COLLECT });
                    }
                    else
                    {
                        selectedCrewTransfers = null;
                    }
                }
            }

            GUILayout.EndScrollView();
            return selectedResources != null || selectedCrewTransfers != null;
        }
    }
}
