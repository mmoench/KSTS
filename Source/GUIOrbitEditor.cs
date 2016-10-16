using System;
using System.Text.RegularExpressions;
using UnityEngine;

namespace KSTS
{
    class GUIOrbitEditor
    {
        private CelestialBody body = null;
        private GUIRichValueSelector altitudeSelector;
        private GUIRichValueSelector inclinationSelector;
        private MissionProfile missionProfile = null;

        public GUIOrbitEditor(MissionProfile missionProfile)
        {
            // TODO: Maybe add a switch for complex and simple orbits, the complex editor might also include an option to copy an orbit from a reference-object ...
            this.body = FlightGlobals.GetHomeBody();
            this.missionProfile = missionProfile;
            this.altitudeSelector = new GUIRichValueSelector("Altitude", Math.Floor(this.missionProfile.maxAltitude), "m", Math.Ceiling(this.missionProfile.minAltitude), Math.Floor(this.missionProfile.maxAltitude), true, "#,##0");
            this.inclinationSelector = new GUIRichValueSelector("Inclination", 0, "°", -180, 180, true, "+0.00;-0.00");
        }

        public static string FormatAltitude(double altitude)
        {
            if (altitude >= 1000000) return (altitude/1000).ToString("#,##0km");
            else return altitude.ToString("#,##0m");
        }

        public static Orbit CreateSimpleOrbit(CelestialBody body, double altitude, double inclination)
        {
            return GUIOrbitEditor.CreateOrbit(inclination, 0, altitude + body.Radius, 0, 0, 0, 0, body);
        }

        public static Orbit CreateOrbit(double inclination, double eccentricity, double semiMajorAxis, double longitudeOfAscendingNode, double argumentOfPeriapsis, double meanAnomalyAtEpoch, double epoch, CelestialBody body)
        {
            if (double.IsNaN(inclination)) inclination = 0;
            if (double.IsNaN(eccentricity)) eccentricity = 0;
            if (double.IsNaN(semiMajorAxis)) semiMajorAxis = body.Radius + body.atmosphereDepth + 10000;
            if (double.IsNaN(longitudeOfAscendingNode)) longitudeOfAscendingNode = 0;
            if (double.IsNaN(argumentOfPeriapsis)) argumentOfPeriapsis = 0;
            if (double.IsNaN(meanAnomalyAtEpoch)) meanAnomalyAtEpoch = 0;
            if (double.IsNaN(epoch)) meanAnomalyAtEpoch = Planetarium.GetUniversalTime();
            if (Math.Sign(eccentricity - 1) == Math.Sign(semiMajorAxis)) semiMajorAxis = -semiMajorAxis;

            if (Math.Sign(semiMajorAxis) >= 0)
            {
                while (meanAnomalyAtEpoch < 0) meanAnomalyAtEpoch += Math.PI * 2;
                while (meanAnomalyAtEpoch > Math.PI * 2) meanAnomalyAtEpoch -= Math.PI * 2;
            }

            return new Orbit(inclination, eccentricity, semiMajorAxis, longitudeOfAscendingNode, argumentOfPeriapsis, meanAnomalyAtEpoch, epoch, body);
        }

        public static ConfigNode SaveOrbitToNode(Orbit orbit, string nodeName="orbit")
        {
            ConfigNode node = new ConfigNode(nodeName);
            node.AddValue("inclination", orbit.inclination);
            node.AddValue("eccentricity", orbit.eccentricity);
            node.AddValue("semiMajorAxis", orbit.semiMajorAxis);
            node.AddValue("longitudeOfAscendingNode", orbit.LAN);
            node.AddValue("argumentOfPeriapsis", orbit.argumentOfPeriapsis);
            node.AddValue("meanAnomalyEpoch", orbit.meanAnomalyAtEpoch);
            node.AddValue("epoch", orbit.epoch);
            node.AddValue("body", orbit.referenceBody.bodyName);
            return node;
        }

        public static Orbit CreateOrbitFromNode(ConfigNode node)
        {
            return CreateOrbit(
                Double.Parse(node.GetValue("inclination")),
                Double.Parse(node.GetValue("eccentricity")),
                Double.Parse(node.GetValue("semiMajorAxis")),
                Double.Parse(node.GetValue("longitudeOfAscendingNode")),
                Double.Parse(node.GetValue("argumentOfPeriapsis")),
                Double.Parse(node.GetValue("meanAnomalyEpoch")),
                Double.Parse(node.GetValue("epoch")),
                FlightGlobals.Bodies.Find(x => x.bodyName == node.GetValue("body"))
            );
        }

        // Checks if the given orbit is already used by another vessel, returns true if it can be used safely:
        public static bool CheckOrbitClear(Orbit orbit, double unsafeDistance=50)
        {
            foreach (Vessel vessel in FlightGlobals.Vessels)
            {
                if (vessel.situation != Vessel.Situations.ORBITING) continue;
                if (vessel.orbit == null || vessel.orbit.referenceBody != orbit.referenceBody) continue;
                double orbitalDistance = OrbitUtil.GetSmaDistance(vessel.orbit, orbit);
                if (Math.Abs(orbitalDistance) < unsafeDistance)
                {
                    Debug.Log("[KSTS] dangerous orbit: " + orbitalDistance.ToString() + "m to " + vessel.vesselName);
                    return false;
                }
            }
            return true;
        }

        public Orbit GetOrbit()
        {
            return GUIOrbitEditor.CreateSimpleOrbit(this.body, this.altitudeSelector.Value, this.inclinationSelector.Value);
        }

        public void DisplayEditor()
        {
            this.altitudeSelector.Display();
            this.inclinationSelector.Display();
        }
    }
}
