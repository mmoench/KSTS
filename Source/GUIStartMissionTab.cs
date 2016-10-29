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
            double currentFunds = KSTS.GetFunds();
            string fundsColor = "#B3D355"; // Green
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

            // Display crew-selector, if the payload can hold kerbals:
            bool selectionIsValid = true;
            if (payloadShipSelector.payload.GetCrewCapacity() > 0)
            {
                GUILayout.Label("");
                GUILayout.Label("<size=14><b>Crew:</b></size>");
                if (!crewTransferSelector.DisplayList()) selectionIsValid = false;
            }

            // Show Button for Flag-Selector:
            GUILayout.Label("");
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
                // Start the mission:
                MissionController.StartMission(Mission.CreateDeployment(shipName, payloadShipSelector.payload.template, orbitEditor.GetOrbit(), missionProfileSelector.selectedProfile, crewTransferSelector.crewToDeliver, flagSelector.flagURL));
                KSTS.AddFunds(-currentCost);
                Reset();
                return true;
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
                KSTS.AddFunds(-currentCost);
                Reset();
                return true;
            }
            return false;
        }
    }

    class GUIStartConstructMissionTab : GUIStartMissionTab
    {
        private static GUIPayloadShipSelector payloadShipSelector = null;
        private static GUIMissionProfileSelector missionProfileSelector = null;
        private static GUITargetVesselSelector targetVesselSelector = null;
        private static GUICrewTransferSelector crewTransferSelector = null;
        private static GUIFlagSelector flagSelector = null;
        private static double currentCost = 0;
        private static Vector2 scrollPos = Vector2.zero;
        private static string shipName = "";
        private static double constructionTime = 0;

        public static void Reset()
        {
            payloadShipSelector = null;
            missionProfileSelector = null;
            targetVesselSelector = null;
            crewTransferSelector = null;
            flagSelector = null;
            scrollPos = Vector2.zero;
            shipName = "";
            constructionTime = 0;
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
                targetVesselSelector = null;
                missionProfileSelector = null;
                crewTransferSelector = null;
                flagSelector = null;
                return false;
            }
            currentCost += payloadShipSelector.payload.template.totalCost;
            double dryMass = payloadShipSelector.payload.GetDryMass();
            double totalMass = payloadShipSelector.payload.template.totalMass;
            int engineersRequired = (int) Math.Ceiling( Math.Log(Math.Ceiling(dryMass / 10)) / Math.Log(2) ) + 1; // One engineer can construct up to 10t, each additional engineer doubles that number

            // Target (space-dock) selection:
            if (targetVesselSelector == null)
            {
                targetVesselSelector = new GUITargetVesselSelector();
                targetVesselSelector.filterVesselType = VesselType.Station;
                targetVesselSelector.filterHasCrewTrait = "Engineer"; // There does not seem to be an enum for this.
                targetVesselSelector.filterHasCrewTraitCount = engineersRequired;
            }
            if (targetVesselSelector.targetVessel == null)
            {
                targetVesselSelector.DisplayList();
                return false;
            }
            if (targetVesselSelector.DisplaySelected())
            {
                missionProfileSelector = null;
                crewTransferSelector = null;
                flagSelector = null;
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
                shipName = payloadShipSelector.payload.template.shipName;
            }
            if (missionProfileSelector.selectedProfile == null)
            {
                missionProfileSelector.DisplayList();
                return false;
            }
            if (missionProfileSelector.DisplaySelected())
            {
                crewTransferSelector = null;
                flagSelector = null;
                return false;
            }

            if (crewTransferSelector == null) crewTransferSelector = new GUICrewTransferSelector(payloadShipSelector.payload, missionProfileSelector.selectedProfile);
            if (flagSelector == null) flagSelector = new GUIFlagSelector();

            // Display Construction-Info:
            scrollPos = GUILayout.BeginScrollView(scrollPos, GUI.scrollStyle);
            GUILayout.Label("<size=14><b>Construction Info:</b></size>");

            GUILayout.BeginHorizontal();
            GUILayout.Label("Ship name:", new GUIStyle(GUI.labelStyle) { stretchWidth = true });
            shipName = GUILayout.TextField(shipName, new GUIStyle(GUI.textFieldStyle) { alignment = TextAnchor.MiddleRight, stretchWidth = false, fixedWidth = 320 });
            GUILayout.EndHorizontal();

            // Calculate and display all the construction-parameters:
            int engineers = TargetVessel.GetCrewCountWithTrait(targetVesselSelector.targetVessel, "Engineer");
            int scientists = TargetVessel.GetCrewCountWithTrait(targetVesselSelector.targetVessel, "Scientist");
            if (engineers < engineersRequired) throw new Exception("not enough engineers on target vessel");
            if (missionProfileSelector.selectedProfile.payloadMass <= 0) throw new Exception("mission profile payload too low");
            int flights = (int) Math.Ceiling(totalMass / missionProfileSelector.selectedProfile.payloadMass);
            double flightTime = missionProfileSelector.selectedProfile.missionDuration;
            double totalFlightTime = flightTime * flights;
            double baseConstructionTime = dryMass * 6 * 60 * 60; // 1 (kerbin-) day / ton
            double totalFlightCost = missionProfileSelector.selectedProfile.launchCost * flights;

            currentCost += totalFlightCost;
            constructionTime = baseConstructionTime;
            if (scientists > 0) constructionTime = baseConstructionTime / (scientists + 1); // half the time per scientist
            if (totalFlightTime > constructionTime) constructionTime = totalFlightTime;

            GUIStyle leftLabel = new GUIStyle(GUI.labelStyle) { stretchWidth = true };
            GUIStyle rightLabel = new GUIStyle(GUI.labelStyle) { stretchWidth = false, alignment = TextAnchor.MiddleRight };
            GUILayout.BeginHorizontal();
            GUILayout.Label("Mass:", leftLabel);
            GUILayout.Label(totalMass.ToString("#,##0.00t") + " / " + dryMass.ToString("#,##0.00t") + " dry", rightLabel);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Dock-Capacity (" + engineers.ToString() + " engineer" + (engineers != 1 ? "s" : "") + "):", leftLabel);
            GUILayout.Label((Math.Pow(2,engineers-1)*10).ToString("#,##0.00t") + " dry", rightLabel);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Single Flight (" + (missionProfileSelector.selectedProfile.launchCost / missionProfileSelector.selectedProfile.payloadMass).ToString("#,##0 √/t") + "):", leftLabel);
            GUILayout.Label(missionProfileSelector.selectedProfile.payloadMass.ToString("#,##0.00t") + " in " + GUI.FormatDuration(flightTime) + " for " + missionProfileSelector.selectedProfile.launchCost.ToString("#,##0 √"), rightLabel);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Total Flights:", leftLabel);
            GUILayout.Label(flights.ToString("#,##0") + " in " + GUI.FormatDuration(totalFlightTime) + " for " + totalFlightCost.ToString("#,##0 √"), rightLabel);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Base Construction Time (6h/t):", leftLabel);
            GUILayout.Label(GUI.FormatDuration(baseConstructionTime), rightLabel);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Total Construction Time (" + scientists.ToString() + " scientist" + (scientists != 1 ? "s" : "") + "):", leftLabel);
            GUILayout.Label(GUI.FormatDuration(constructionTime), rightLabel);
            GUILayout.EndHorizontal();

            // Display crew-selector, if the new ship can hold kerbals:
            bool selectionIsValid = true;
            if (payloadShipSelector.payload.GetCrewCapacity() > 0)
            {
                GUILayout.Label("");
                GUILayout.Label("<size=14><b>Crew:</b></size>");
                if (!crewTransferSelector.DisplayList()) selectionIsValid = false;
            }

            // Show Button for Flag-Selector:
            GUILayout.Label("");
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
                // Start the mission:
                MissionController.StartMission(Mission.CreateConstruction(shipName, payloadShipSelector.payload.template, targetVesselSelector.targetVessel, missionProfileSelector.selectedProfile, crewTransferSelector.crewToDeliver, flagSelector.flagURL, constructionTime));
                KSTS.AddFunds(-currentCost);
                Reset();
                return true;
            }
            return false;
        }
    }
}
