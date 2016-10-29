using System;
using System.Collections.Generic;
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

        // Retrurns a new orbit, which is following the given orbit at the given distance:
        public static Orbit CreateFollowingOrbit(Orbit referenceOrbit, double distance)
        {
            Orbit orbit = CreateOrbit(referenceOrbit.inclination, referenceOrbit.eccentricity, referenceOrbit.semiMajorAxis, referenceOrbit.LAN, referenceOrbit.argumentOfPeriapsis, referenceOrbit.meanAnomalyAtEpoch, referenceOrbit.epoch, referenceOrbit.referenceBody);
            // The distance ("chord") between to points on a circle is given by: chord = 2r * sin( alpha / 2 )
            double angle = Math.Sinh(distance / (2 * orbit.semiMajorAxis)) * 2; // Find the angle for the given distance
            orbit.meanAnomalyAtEpoch += angle;
            return orbit;
        }

        // Modifies and returns the given orbit so that a vessel of the given size won't collide with any other vessel on the same orbit:
        public static Orbit ApplySafetyDistance(Orbit orbit, float vesselSize)
        {
            // Find out how many degrees one meter is on the given orbit (same formula as above):
            double anglePerMeters = Math.Sinh(1.0 / (2 * orbit.semiMajorAxis)) * 2;

            // Check with every other vessel on simmilar orbits, if they might collide in the future:
            System.Random rnd = new System.Random();
            int adjustmentIterations = 0;
            bool orbitAdjusted;
            do
            {
                orbitAdjusted = false;
                foreach (Vessel vessel in FlightGlobals.Vessels)
                {
                    if (vessel.situation != Vessel.Situations.ORBITING) continue;
                    if (vessel.orbit.referenceBody != orbit.referenceBody) continue;

                    // Find the next rendezvous (most of these parameters are just guesses, but they seem to work):
                    double UT = Planetarium.GetUniversalTime();
                    double dT = 86400;
                    double threshold = 5000;
                    double MinUT = Planetarium.GetUniversalTime() - 86400;
                    double MaxUT = Planetarium.GetUniversalTime() + 86400;
                    double epsilon = 360;
                    int maxIterations = 25;
                    int iterationCount = 0;
                    vessel.orbit.UpdateFromUT(Planetarium.GetUniversalTime()); // We apparently have to update both orbits to the current time to make this work.
                    orbit.UpdateFromUT(Planetarium.GetUniversalTime());
                    double closestApproach = Orbit._SolveClosestApproach(vessel.orbit, orbit, ref UT, dT, threshold, MinUT, MaxUT, epsilon, maxIterations, ref iterationCount);
                    if (closestApproach < 0) continue; // No contact
                    if (closestApproach > 10000) continue; // 10km should be fine

                    double blockerSize = TargetVessel.GetVesselSize(vessel);
                    if (closestApproach < (blockerSize + vesselSize) / 2) // We assume the closest approach is calculated from the center of mass, which is why we use /2
                    {
                        // Adjust orbit:
                        double adjustedAngle = (blockerSize / 2 + vesselSize / 2 + 1) * anglePerMeters; // Size of both vessels + 1m
                        if (adjustmentIterations >= 90) adjustedAngle *= rnd.Next(1, 1000); // Lets get bolder here, time is running out ...

                        // Modifying "orbit.meanAnomalyAtEpoch" works for the actual vessel, but apparently one would have to call some other method as well to updated
                        // additional internals of the object, because the "_SolveClosestApproach"-function does not register this change, which is why we simply create
                        // a new, modified orbit:
                        orbit = CreateOrbit(orbit.inclination, orbit.eccentricity, orbit.semiMajorAxis, orbit.LAN, orbit.argumentOfPeriapsis, orbit.meanAnomalyAtEpoch + adjustedAngle, orbit.epoch, orbit.referenceBody);

                        orbitAdjusted = true;
                        Debug.Log("[KSTS] adjusting planned orbit by " + adjustedAngle + "° to avoid collision with '" + vessel.vesselName + "' (closest approach " + closestApproach.ToString() + "m @ " + UT.ToString() + " after " + iterationCount + " orbits)");
                    }
                }
                adjustmentIterations++;
                if (adjustmentIterations >= 100 && orbitAdjusted)
                {
                    Debug.LogError("[KSTS] unable to find a safe orbit after " + adjustmentIterations.ToString() + " iterations, the vessels will likely crash");
                    break;
                }
            }
            while (orbitAdjusted);
            return orbit;
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
