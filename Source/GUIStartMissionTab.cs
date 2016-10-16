using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace KSTS
{

    // Base class with common functions for all the other start-mission tabs:
    abstract class GUIStartMissionTab
    {
        // Displays the footer with the current cost and maybe an "execute"-button; returns true, if that button was pressed:
        protected static bool DisplayFooter(double cost, bool displayButton)
        {
            double currentFunds = 0;
            string fundsColor = "#B3D355"; // Green
            if (Funding.Instance != null) currentFunds = Funding.Instance.Funds;
            if (cost > currentFunds) fundsColor = "#D35555"; // Red

            GUILayout.BeginHorizontal();
            GUILayout.Label("<size=14><b>Total Mission-Cost: <color=" + fundsColor + ">" + cost.ToString("#,##0 √") + "</color></b></size>", new GUIStyle(GUI.labelStyle) { stretchWidth = true });
            if (cost <= currentFunds && displayButton)
            {
                if (GUILayout.Button("Start Mission", new GUIStyle(GUI.buttonStyle) { stretchWidth = false })) return true;
            }
            GUILayout.EndHorizontal();
            return false;
        }
    }

    class GUIStartDeployMissionTab : GUIStartMissionTab
    {
        private static GUIPayloadShipSelector payloadShipSelector = null;
        private static GUIMissionProfileSelector missionProfileSelector = null;
        private static GUIOrbitEditor orbitEditor = null;
        private static GUICrewTransferSelector crewTransferSelector = null;
        private static GUIFlagSelector flagSelector = null;
        private static double currentCost = 0;
        private static Vector2 scrollPos = Vector2.zero;
        private static string shipName = "";

        public static void Reset()
        {
            payloadShipSelector = null;
            missionProfileSelector = null;
            orbitEditor = null;
            crewTransferSelector = null;
            flagSelector = null;
            scrollPos = Vector2.zero;
            shipName = "";
        }

        private static bool DisplayInner()
        {
            // Payload selection:
            if (payloadShipSelector == null) payloadShipSelector = new GUIPayloadShipSelector();
            if (payloadShipSelector.payload == null)
            {
                payloadShipSelector.DisplayList();
                return false;
            }
            if (payloadShipSelector.DisplaySelected())
            {
                missionProfileSelector = null;
                orbitEditor = null;
                crewTransferSelector = null;
                flagSelector = null;
                return false;
            }
            currentCost += payloadShipSelector.payload.template.totalCost;

            // Mission-Profile selection:
            if (missionProfileSelector == null)
            {
                missionProfileSelector = new GUIMissionProfileSelector();
                missionProfileSelector.filterMass = payloadShipSelector.payload.template.totalMass;
                missionProfileSelector.filterMissionType = MissionProfileType.DEPLOY;
                shipName = payloadShipSelector.payload.template.shipName;
            }
            if (missionProfileSelector.selectedProfile == null)
            {
                missionProfileSelector.DisplayList();
                return false;
            }
            if (missionProfileSelector.DisplaySelected())
            {
                orbitEditor = null;
                crewTransferSelector = null;
                flagSelector = null;
                return false;
            }
            currentCost += missionProfileSelector.selectedProfile.launchCost;

            // Mission-Parameters selection:
            if (orbitEditor == null) orbitEditor = new GUIOrbitEditor(missionProfileSelector.selectedProfile);
            if (crewTransferSelector == null) crewTransferSelector = new GUICrewTransferSelector(payloadShipSelector.payload, missionProfileSelector.selectedProfile);
            if (flagSelector == null) flagSelector = new GUIFlagSelector();
            scrollPos = GUILayout.BeginScrollView(scrollPos, GUI.scrollStyle);

            GUILayout.Label("<size=14><b>Mission Parameters:</b></size>");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Ship name:", new GUIStyle(GUI.labelStyle) { stretchWidth = true });
            shipName = GUILayout.TextField(shipName, new GUIStyle(GUI.textFieldStyle) { alignment = TextAnchor.MiddleRight, stretchWidth = false, fixedWidth = 320 });
            GUILayout.EndHorizontal();

            orbitEditor.DisplayEditor();

            // Display crew-selector, if the payload cat hold kerbals:
            bool selectionIsValid = true;
            if (payloadShipSelector.payload.GetCrewCapacity() > 0)
            {
                if (!crewTransferSelector.DisplayList()) selectionIsValid = false;
            }

            // Show Button for Flag-Selector:
            flagSelector.ShowButton();

            GUILayout.EndScrollView();
            return selectionIsValid;
        }

        public static bool Display()
        {
            currentCost = 0;
            bool ready = DisplayInner();
            bool launch = DisplayFooter(currentCost, ready);
            if (launch)
            {
                if (!GUIOrbitEditor.CheckOrbitClear(orbitEditor.GetOrbit()))
                {
                    // The selected orbit is used by another vessel, abort:
                    ScreenMessages.PostScreenMessage("Selected orbit already in use by another vessel, aborting mission!");
                }
                else
                {
                    // The orbit is clear, start the mission:
                    MissionController.StartMission(Mission.CreateDeployment(shipName, payloadShipSelector.payload.template, orbitEditor.GetOrbit(), missionProfileSelector.selectedProfile, crewTransferSelector.crewToDeliver, flagSelector.flagURL));
                    if (Funding.Instance != null) Funding.Instance.AddFunds(-currentCost, TransactionReasons.VesselRollout);
                    Reset();
                    return true;
                }
            }
            return false;
        }
    }

    class GUIStartTransportMissionTab : GUIStartMissionTab
    {
        private static GUITargetVesselSelector targetVesselSelector = null;
        private static GUIMissionProfileSelector missionProfileSelector = null;
        private static GUITransportSelector payloadResourceSelector = null;
        private static double currentCost = 0;
        private static Vector2 scrollPos = Vector2.zero;

        public static void Reset()
        {
            targetVesselSelector = null;
            missionProfileSelector = null;
            payloadResourceSelector = null;
            scrollPos = Vector2.zero;
        }

        private static bool DisplayInner()
        {
            // Target selection:
            if (targetVesselSelector == null) targetVesselSelector = new GUITargetVesselSelector();
            if (targetVesselSelector.targetVessel == null)
            {
                targetVesselSelector.DisplayList();
                return false;
            }
            if (targetVesselSelector.DisplaySelected())
            {
                missionProfileSelector = null;
                payloadResourceSelector = null;
                return false;
            }

            // Mission-Profile selection:
            if (missionProfileSelector == null)
            {
                missionProfileSelector = new GUIMissionProfileSelector();
                missionProfileSelector.filterAltitude = targetVesselSelector.targetVessel.orbit.ApA;
                missionProfileSelector.filterBody = targetVesselSelector.targetVessel.orbit.referenceBody;
                missionProfileSelector.filterDockingPortTypes = TargetVessel.GetVesselDockingPortTypes(targetVesselSelector.targetVessel);
                missionProfileSelector.filterMissionType = MissionProfileType.TRANSPORT;
            }
            if (missionProfileSelector.selectedProfile == null)
            {
                missionProfileSelector.DisplayList();
                return false;
            }
            if (missionProfileSelector.DisplaySelected(GUIMissionProfileSelector.SELECTED_DETAILS_PAYLOAD))
            {
                payloadResourceSelector = null;
                return false;
            }
            currentCost += missionProfileSelector.selectedProfile.launchCost;

            // Payload-Resource selection:
            if (payloadResourceSelector == null)
            {
                payloadResourceSelector = new GUITransportSelector(targetVesselSelector.targetVessel,missionProfileSelector.selectedProfile);
            }
            payloadResourceSelector.DisplayList(); // Always display this selector (it has to be the last), but return when nothing is selected.

            if (payloadResourceSelector.selectedResources != null)
            {
                // Determine the cost of the selected resources:
                foreach (PayloadResource payloadResource in payloadResourceSelector.selectedResources)
                {
                    currentCost += KSTS.resourceDictionary[payloadResource.name].unitCost * payloadResource.amount;
                }
                return true;
            }
            else if (payloadResourceSelector.selectedCrewTransfers != null) return true;
            return false;
        }

        public static bool Display()
        {
            currentCost = 0;
            bool ready = DisplayInner();
            bool launch = DisplayFooter(currentCost, ready);
            if (launch)
            {
                MissionController.StartMission(Mission.CreateTransport(
                    targetVesselSelector.targetVessel,
                    missionProfileSelector.selectedProfile,
                    payloadResourceSelector.selectedResources,
                    payloadResourceSelector.selectedCrewTransfers
                ));
                if (Funding.Instance != null) Funding.Instance.AddFunds(-currentCost, TransactionReasons.VesselRollout);
                Reset();
                return true;
            }
            return false;
        }
    }
}
