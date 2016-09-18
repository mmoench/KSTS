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

        // Displays the currently selected target-vessel and returns true, if the player has deselected it:
        public bool DisplaySelected()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("<size=14><b>Target:</b></size>", new GUIStyle(GUI.labelStyle) { stretchWidth = true });
            if (GUILayout.Button("<size=14><color=#F9FA86><b>" + targetVessel.vesselName.ToString() + "</b></color> (Apoapsis: " + targetVessel.orbit.ApA.ToString("#,##0m") + ")</size>", new GUIStyle(GUI.buttonStyle) { alignment = TextAnchor.MiddleRight, stretchWidth = false, fixedWidth = 320 }))
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
                foreach (Vessel vessel in validTargets)
                {
                    if (!TargetVessel.IsValidTarget(vessel)) continue;
                    List<string> descriptions = new List<string>();
                    descriptions.Add("<color=#F9FA86><b>" + vessel.vesselName + "</b></color><color=#FFFFFF>");

                    // Orbital-Parameters:
                    descriptions.Add("<b>Apoapsis:</b> " + vessel.orbit.ApA.ToString("#,##0m") + ", <b>Periapsis:</b> " + vessel.orbit.PeA.ToString("#,##0m") + ", <b>MET:</b> " + GUI.FormatDuration(vessel.missionTime));

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
                    int crew = vessel.GetVesselCrew().Count;
                    if (seats > 0) descriptions.Add("<b>Crew:</b> " + crew.ToString() + "/" + seats.ToString());

                    contents.Add(new GUIContent(String.Join("\n", descriptions.ToArray()) + "</color>"));
                }
                if ((selectedIndex = GUILayout.SelectionGrid(selectedIndex, contents.ToArray(), 1, GUI.selectionGridStyle)) >= 0)
                {
                    // The player has selected a payload:
                    targetVessel = validTargets[selectedIndex];
                }
            }
            GUILayout.EndScrollView();
            return targetVessel != null;
        }
    }
}
