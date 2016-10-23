using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace KSTS
{
    class GUITargetVesselSelector
    {
        private Vector2 scrollPos = Vector2.zero;
        private int selectedIndex = -1;
        public Vessel targetVessel = null;

        public VesselType? filterVesselType = null;
        public string filterHasCrewTrait = null;

        // Displays the currently selected target-vessel and returns true, if the player has deselected it:
        public bool DisplaySelected()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("<size=14><b>Target:</b></size>", new GUIStyle(GUI.labelStyle) { stretchWidth = true });
            if (GUILayout.Button("<size=14><color=#F9FA86><b>" + targetVessel.vesselName.ToString() + "</b></color> (Apoapsis: " + GUI.FormatAltitude(targetVessel.orbit.ApA) + ")</size>", new GUIStyle(GUI.buttonStyle) { alignment = TextAnchor.MiddleRight, stretchWidth = false, fixedWidth = 320 }))
            {
                selectedIndex = -1;
                targetVessel = null;
            }
            GUILayout.EndHorizontal();
            return targetVessel == null;
        }

        // Shows a list of all available target-vessels and returns true, if the player has selected one:
        public bool DisplayList()
        {
            GUILayout.Label("<size=14><b>Target:</b></size>");
            scrollPos = GUILayout.BeginScrollView(scrollPos, GUI.scrollStyle);
            string green = "#00FF00";
            string red = "#FF0000";

            // Build a list with all valid vessels:
            List<Vessel> validTargets = new List<Vessel>();
            foreach (Vessel vessel in FlightGlobals.Vessels)
            {
                if (!TargetVessel.IsValidTarget(vessel)) continue;
                validTargets.Add(vessel);
            }
            if (selectedIndex >= validTargets.Count)
            {
                selectedIndex = -1;
                targetVessel = null;
            }

            if (validTargets.Count == 0)
            {
                // No valid targest available:
                GUILayout.Label("No valid targets in orbit.");
            }
            else
            {
                // Show list with all possible vessels:
                List<GUIContent> contents = new List<GUIContent>();
                List<int> filteredIndices = new List<int>(); // Target-vessels which fall out of the defined filters will get noted here.
                int index = 0;
                foreach (Vessel vessel in validTargets)
                {
                    bool filterThisTarget = false;
                    if (!TargetVessel.IsValidTarget(vessel)) continue;
                    List<string> descriptions = new List<string>();
                    descriptions.Add("<color=#F9FA86><b>" + vessel.vesselName + "</b></color><color=#FFFFFF>");

                    // Orbital-Parameters:
                    descriptions.Add("<b>Apoapsis:</b> " + GUI.FormatAltitude(vessel.orbit.ApA) + ", <b>Periapsis:</b> " + GUI.FormatAltitude(vessel.orbit.PeA) + ", <b>MET:</b> " + GUI.FormatDuration(vessel.missionTime));

                    // Docking-Port Types:
                    List<string> dockingPortsTranslated = new List<string>();
                    foreach (string dockingPortType in TargetVessel.GetVesselDockingPortTypes(vessel)) dockingPortsTranslated.Add(TargetVessel.TranslateDockingPortName(dockingPortType));
                    if (dockingPortsTranslated.Count > 0) descriptions.Add("<b>Docking-Ports:</b> " + string.Join(", ", dockingPortsTranslated.ToArray()));

                    // Resources:
                    double capacity = 0;
                    foreach(PayloadResource availableResource in TargetVessel.GetFreeResourcesCapacities(vessel)) capacity += availableResource.amount * availableResource.mass;
                    if (capacity > 0) descriptions.Add("<b>Resource Capacity:</b> " + capacity.ToString("#,##0.00t"));

                    // Crew:
                    int seats = TargetVessel.GetCrewCapacity(vessel);
                    int crew = TargetVessel.GetCrew(vessel).Count;
                    if (seats > 0) descriptions.Add("<b>Crew:</b> " + crew.ToString() + "/" + seats.ToString());

                    // Maybe apply additional filters and show their attributes:
                    List<string> filterAttributes = new List<string>(); ;
                    if (filterVesselType != null)
                    {
                        bool isValidType = vessel.vesselType == (VesselType)filterVesselType;
                        string color = isValidType ? green : red;
                        filterAttributes.Add("<b>Type:</b> <color=" + color + ">" + vessel.vesselType.ToString() + "</color>");
                        if (!isValidType) filterThisTarget = true;
                    }
                    if (filterHasCrewTrait != null)
                    {
                        int traitCount = TargetVessel.GetCrewCountWithTrait(vessel, filterHasCrewTrait);
                        string color = traitCount > 0 ? green : red;
                        filterAttributes.Add("<b>" + filterHasCrewTrait + "s:</b> <color=" + color + ">" + traitCount.ToString() + "</color>");
                        if (traitCount == 0) filterThisTarget = true;
                    }
                    if (filterAttributes.Count > 0) descriptions.Add(String.Join(" ", filterAttributes.ToArray()));

                    contents.Add(new GUIContent(String.Join("\n", descriptions.ToArray()) + "</color>"));
                    if (filterThisTarget) filteredIndices.Add(index); // If there were filters, which did not match, we still show the target, but don't allow to select it.
                    index++;
                }

                int newSelection = GUILayout.SelectionGrid(selectedIndex, contents.ToArray(), 1, GUI.selectionGridStyle);
                if (newSelection >= 0 && !filteredIndices.Contains(newSelection))
                {
                    // The player has selected a payload:
                    selectedIndex = newSelection;
                    targetVessel = validTargets[selectedIndex];
                }
            }
            GUILayout.EndScrollView();
            return targetVessel != null;
        }
    }
}
