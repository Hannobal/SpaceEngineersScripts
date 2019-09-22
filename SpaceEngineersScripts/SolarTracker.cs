#region pre_script

using System;
using System.Collections.Generic;
using VRageMath;
using VRage.Game;
using VRage.Library;
using System.Text;
using Sandbox.ModAPI.Interfaces;
using Sandbox.ModAPI.Ingame;
using Sandbox.Common;
using Sandbox.Game;
using VRage.Collections;
using VRage.Game.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;

namespace Scripts
{
    class ProgramASFD : MyGridProgram
    {
        #endregion pre_script


        /**********************************************************************
         * Every Solar tracker must be a group with the following requirements:
         * - group name must contain the nameTag (define below)
         * - group name must contain one rotor with its name containing the
         *   string "Azimuth" or "Theta"
         * - group name must contain one rotor with its name containing the
         *   string "Inclination" or "Phi"
         * - at least one solar panel must be in the group
         * - [optional:] a text panel for output
         * 
         * possible arguments:
         * - "refresh" : updates the list of solar trackers
         *               run this if you add/mofiy a tracker
         * 
         * Note: I recommend to set limits for the Inclination/Theta rotor
         * just in case the tracker goes haywire (which it shouldn't...)
         * 
         * Note: Although the solar panel output is updated every 10 game
         * tics, the tracker is only updated every 300 tics. This is because
         * large panels (I usually build 5x5-1=24 solar panels on large grids.)
         * often vibrate a bit when a rotor is inverted and that causes
         * fluctuations in the output, which the very simple minimization
         * algorithm employed here can not cope with. The large interval gives
         * the panel time to stabilize and yields a sufficiently large
         * difference of the solar panel ouptut.
         *********************************************************************/

        static readonly int updateFrequency = 3; // every 300 game tics
        static readonly float maxRotorVel = 0.01f;
        static readonly string nameTag = "SolarTracker";

        int updateCounter=0;
        List<SolarTracker> solarTrackers;

        class TrackerRotor
        {
            public TrackerRotor(IMyMotorStator rotor_)
            {
                rotor = rotor_;
                lastVelocityPositive = rotor_.TargetVelocityRad >= 0 ? true : false;
            }

            public void Stop()
            {
                if (!IsWorking) return;
                rotor.TargetVelocityRad = 0;
            }

            public void StartSameDirection()
            {
                if (!IsWorking) return;
                rotor.TargetVelocityRad = lastVelocityPositive ? maxRotorVel : -maxRotorVel;
            }

            public void StartReverseDirection()
            {
                if (!IsWorking) return;
                rotor.TargetVelocityRad = lastVelocityPositive ? -maxRotorVel : maxRotorVel;
                lastVelocityPositive ^= true;
            }

            public bool IsWorking
            {
                get
                {
                    if (rotor == null) return false;
                    return rotor.IsWorking;
                }
            }

            public bool IsMoving
            {
                get { return rotor.TargetVelocityRad == 0f; }
            }

            public IMyMotorStator Rotor
            {
                get { return rotor; }
            }

            private IMyMotorStator rotor = null;
            private bool lastVelocityPositive = true;
            public bool wasReversed = false;

            bool LastVelocityPositive
            {
                get { return lastVelocityPositive; }
            }

        };

        class SolarTracker
        {
            public SolarTracker(
                IMySolarPanel solarPanel_,
                IMyMotorStator rotorInclination_,
                IMyMotorStator rotorAzimuth_,
                IMyTextPanel textPanel_ = null
                )
            {
                solarPanel = solarPanel_;
                rotorAzimuth = new TrackerRotor(rotorAzimuth_);
                rotorInclination = new TrackerRotor(rotorInclination_);
                textPanel = textPanel_;
                previousPowerOutput = solarPanel.MaxOutput;
                Initialize();
            }

            void Initialize()
            {
                if (rotorInclination.IsMoving)
                {
                    activeRotor = rotorInclination;
                    rotorAzimuth.Stop();
                }
                else if (rotorAzimuth.IsMoving)
                {
                    activeRotor = rotorAzimuth;
                }
                else
                {
                    activeRotor = rotorInclination;
                }
            }

            public void Update()
            {
                if (textPanel != null) textPanel.WriteText("");

                if (!IsWorking) return;
                if (solarPanel.MaxOutput == 0f) // it's probably night
                {
                    previousPowerOutput = solarPanel.MaxOutput;
                    activeRotor.Stop();
                    return;
                }

                float diff = 1.0e6f * (solarPanel.MaxOutput - previousPowerOutput);

                if (diff >= 0)
                {
                    Print("Continueing");
                    Print(diff.ToString());
                    activeRotor.StartSameDirection();
                }
                else if (activeRotor.wasReversed)
                {
                    Print("swapping rotor");
                    Print(diff.ToString());
                    // the output is getting worse, but we already reversed the
                    // rotation direction of the rotor. Hence, we stop this and
                    // start the other rotor
                    activeRotor.Stop();
                    activeRotor = activeRotor == rotorInclination ? rotorAzimuth : rotorInclination;
                    activeRotor.wasReversed = false;
                    activeRotor.StartReverseDirection();
                }
                else
                {
                    Print("Changing direction ");
                    Print(diff.ToString());
                    // the output is getting worse, so we turn the rotor the other way
                    activeRotor.StartReverseDirection();
                    activeRotor.wasReversed = true;
                }

                UpdateTextPanel();

                previousPowerOutput = solarPanel.MaxOutput;
            }

            public bool IsWorking
            {
                get
                {
                    if (solarPanel == null)
                    {
                        Print("solarPanel == null");
                        return false;
                    }
                    if (rotorInclination == null)
                    {
                        Print("rotorInclination == null");
                        return false;
                    }
                    if (rotorAzimuth == null)
                    {
                        Print("rotorAzimuth == null");
                        return false;
                    }
                    if (!solarPanel.IsWorking)
                    {
                        Print("solarPanel not working");
                        return false;
                    }
                    if (!rotorInclination.IsWorking)
                    {
                        Print("rotorInclination not working");
                        return false;
                    }
                    if (!rotorAzimuth.IsWorking)
                    {
                        Print("rotorAzimuth not working");
                        return false;
                    }
                    return true;
                }
            }

            private void Print(string str)
            {
                if (textPanel == null) return;
                textPanel.WriteText(str + "\n", true);
            }

            private void UpdateTextPanel()
            {
                if (textPanel == null) return;
                textPanel.WriteText(GetInfo(), true);
            }

            public string GetInfo()
            {
                string str = "Power : ";
                str += (1000.0f * solarPanel.MaxOutput).ToString();
                str += " kW\n";
                str += activeRotor.Rotor.CustomName;
                if (activeRotor.wasReversed)
                    str += " - ";
                else
                    str += " + ";
                str += (activeRotor.Rotor.Angle * 57.2957914f).ToString();
                return str;
            }

            private IMySolarPanel solarPanel = null;
            private IMyTextPanel textPanel = null;
            private TrackerRotor rotorInclination = null;
            private TrackerRotor rotorAzimuth = null;
            private TrackerRotor activeRotor = null;
            private float previousPowerOutput = 0f;
        };

        void RefreshSolarTrackerList()
        {
            solarTrackers.Clear();
            var groups = new List<IMyBlockGroup>();
            GridTerminalSystem.GetBlockGroups(groups);

            foreach (IMyBlockGroup group in groups)
            {
                if (!group.Name.Contains(nameTag)) continue;

                List<IMySolarPanel> solarPanels = new List<IMySolarPanel>();
                group.GetBlocksOfType<IMySolarPanel>(solarPanels);
                Echo("Group " + group.Name+":");
                if (solarPanels.Count == 0)
                {
                    Echo("Contains no solar panel");
                    continue;
                }

                List<IMyMotorStator> rotors = new List<IMyMotorStator>();
                group.GetBlocksOfType<IMyMotorStator>(rotors);
                IMyMotorStator rotorAzimuth = null;
                IMyMotorStator rotorInclination = null;
                foreach (IMyMotorStator rotor in rotors)
                {
                    if (rotor.CustomName.Contains("Azimuth")
                        || rotor.CustomName.Contains("Phi"))
                    {
                        if (rotor.CubeGrid == Me.CubeGrid)
                            rotorAzimuth = rotor;
                        else
                            Echo("Rotor for azimuth \""
                                +rotor.CustomName
                                +"\" is in a different grid");
                    }
                    else if (rotor.CustomName.Contains("Inclination")
                        || rotor.CustomName.Contains("Theta"))
                        rotorInclination = rotor;
                }
                if (rotorAzimuth == null)
                {
                    Echo("Contains no Azimuth/Phi rotor");
                    continue;
                }
                if (rotorInclination == null)
                {
                    Echo("Contains no Inclination/Theta rotor");
                    continue;
                }

                List<IMyTextPanel> textPanels = new List<IMyTextPanel>();
                group.GetBlocksOfType<IMyTextPanel>(textPanels);
                IMyTextPanel textPanel = null;
                if (textPanels.Count > 0)
                    textPanel = textPanels[0];

                solarTrackers.Add(
                    new SolarTracker(
                        solarPanels[0],
                        rotorInclination,
                        rotorAzimuth,
                        textPanel
                    )
                );

                Echo("Is a valid solar tracker");
            }
        }

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update100;
            solarTrackers = new List<SolarTracker>();
        }

        public void Load() { }

        public void Save() { }

        void Main(string cmd = "")
        {
            if (cmd == "refresh" || solarTrackers.Count == 0)
            {
                RefreshSolarTrackerList();
            }

            if (updateCounter != 0) return;

            foreach (SolarTracker tracker in solarTrackers)
            {
                tracker.Update();
            }

            updateCounter = (++updateCounter % updateFrequency);
        }

        #region post_script
    }
}
#endregion post_script