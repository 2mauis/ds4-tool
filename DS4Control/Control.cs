﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DS4Library;
namespace DS4Control
{
    public class Control
    {
        X360Device x360Bus;
        DS4Device[] DS4Controllers = new DS4Device[4];
        TPadModeSwitcher[] modeSwitcher = new TPadModeSwitcher[4];
        private bool running = false;
        private DS4State[] MappedState = new DS4State[4];
        private DS4State[] CurrentState = new DS4State[4];
        private DS4State[] PreviousState = new DS4State[4];
        public event EventHandler<DebugEventArgs> Debug = null;

        private class X360Data
        {
            public byte[] Report = new byte[28];
            public byte[] Rumble = new byte[8];
        }
        private X360Data[] processingData = new X360Data[4];

        public Control()
        {
            x360Bus = new X360Device();
            for (int i = 0; i < DS4Controllers.Length; i++)
            {
                processingData[i] = new X360Data();
                MappedState[i] = new DS4State();
                CurrentState[i] = new DS4State();
                PreviousState[i] = new DS4State();
            }
        }

        public bool Start()
        {
            if (x360Bus.Open() && x360Bus.Start())
            {
                LogDebug("Starting...");
                DS4Devices.isExclusiveMode = Global.getUseExclusiveMode();
                LogDebug("Searching for controllers....");
                LogDebug("Using " + (DS4Devices.isExclusiveMode ? "Exclusive Mode": "Shared Mode"));
                try
                {
                    DS4Devices.findControllers();
                    IEnumerable<DS4Device> devices = DS4Devices.getDS4Controllers();
                    int ind = 0;
                    foreach (DS4Device device in devices)
                    {
                        LogDebug("Found Controller: " + device.MacAddress);
                        DS4Controllers[ind] = device;
                        device.Report += this.On_Report;
                        device.Removal += this.On_DS4Removal;
                        TPadModeSwitcher m_switcher = new TPadModeSwitcher(device, ind);
                        m_switcher.Debug += OnDebug;
                        m_switcher.setMode(Global.getTouchEnabled(ind) ? 1 : 0);
                        modeSwitcher[ind] = m_switcher;
                        DS4Color color = Global.loadColor(ind);
                        device.LightBarColor = color;
                        x360Bus.Plugin(ind + 1);
                        ind++;
                        LogDebug("Controller: " + device.MacAddress + " is ready to use");
                    }
                }
                catch (Exception e)
                {
                    LogDebug(e.Message);
                }
                running = true;
                    
            }
            return true;
        }

        public bool Stop()
        {
            if (running)
            {
                running = false;
                LogDebug("Stopping X360 Controllers");
                for (int i = 0; i < DS4Controllers.Length; i++)
                {
                    x360Bus.Unplug(i + 1);
                    DS4Controllers[i] = null;
                    modeSwitcher[i] = null;
                }
                x360Bus.Stop();
                LogDebug("Stopping DS4 Controllers");
                DS4Devices.stopControllers();
                LogDebug("Stopped DS4 Tool");
            }
            return true;

        }

        public bool HotPlug()
        {
            if(running)
            {
                DS4Devices.findControllers();
                IEnumerable<DS4Device> devices = DS4Devices.getDS4Controllers();
                foreach (DS4Device device in devices)
                {
                    if (((Func<bool>)delegate
                    {
                        for (Int32 Index = 0; Index < DS4Controllers.Length; Index++)
                            if (DS4Controllers[Index] != null &&
                                DS4Controllers[Index].MacAddress == device.MacAddress)
                                return true;
                        return false;
                    })())
                        continue;
                    for (Int32 Index = 0; Index < DS4Controllers.Length; Index++)
                        if (DS4Controllers[Index] == null)
                        {
                            DS4Controllers[Index] = device;
                            device.Report += this.On_Report;
                            device.Removal += this.On_DS4Removal;
                            TPadModeSwitcher m_switcher = new TPadModeSwitcher(device, Index);
                            m_switcher.Debug += OnDebug;
                            modeSwitcher[Index] = m_switcher;
                            m_switcher.setMode(Global.getTouchEnabled(Index) ? 1 : 0);
                            device.LightBarColor = Global.loadColor(Index);
                            x360Bus.Plugin(Index + 1);
                            break;
                        }
                }
            }
            return true;
        }

        public string getDS4ControllerInfo(int index)
        {
            if (DS4Controllers[index] != null)
            {
                DS4Device d = DS4Controllers[index];
                return d.MacAddress + ", Battery = " + d.Battery + "%," + " Touchpad = " + modeSwitcher[index].ToString() + " (" + d.ConnectionType + ")";
            }
            else
                return null;
        }

        //Called when DS4 is disconnected or timed out
        protected virtual void On_DS4Removal(object sender, EventArgs e)
        {
            DS4Device device = (DS4Device)sender;
            int ind = -1;
            for (int i = 0; i < DS4Controllers.Length; i++)
                if (DS4Controllers[i] != null && device.MacAddress == DS4Controllers[i].MacAddress)
                    ind = i;
            if (ind != -1)
            {
                x360Bus.Unplug(ind + 1);
                LogDebug("Controller " + device.MacAddress + " was removed or lost connection");
                DS4Controllers[ind] = null;
            }
        }

        //Called every time the new input report has arrived
        protected virtual void On_Report(object sender, EventArgs e)
        {

            DS4Device device = (DS4Device)sender;

            int ind=-1;
            for (int i=0; i<DS4Controllers.Length; i++)
                if(device == DS4Controllers[i])
                    ind = i;

            if (ind!=-1)
            {
                 DS4State cState;
                if (modeSwitcher[ind].getCurrentMode() is ButtonMouse)
                {
                    ButtonMouse mode = (ButtonMouse)modeSwitcher[ind].getCurrentMode();
                    cState = mode.getDS4State();
                }
                else
                {
                    device.getCurrentState(CurrentState[ind]); 
                    cState = CurrentState[ind];
                }
                device.getPreviousState(PreviousState[ind]);
                DS4State pState = PreviousState[ind];
               

                CheckForHotkeys(ind, cState, pState);

                DS4LightBar.updateBatteryStatus(cState.Battery, device, ind);
                
                if (Global.getHasCustomKeysorButtons(ind))
                {
                    Mapping.mapButtons(cState, pState, MappedState[ind]);
                    cState = MappedState[ind];
                }

                x360Bus.Parse(cState, processingData[ind].Report, ind);
                if (x360Bus.Report(processingData[ind].Report, processingData[ind].Rumble))
                {
                    Byte Big = (Byte)(processingData[ind].Rumble[3]);
                    Byte Small = (Byte)(processingData[ind].Rumble[4]);

                    if (processingData[ind].Rumble[1] == 0x08)
                    {
                        setRumble(Small, Big, ind);
                    }
                }
            }
        }


        protected virtual void CheckForHotkeys(int deviceID, DS4State cState, DS4State pState)
        {
            DS4Device d = DS4Controllers[deviceID];
            if (cState.Touch1 && !pState.Share && !pState.Options)
            {
                if (cState.Share)
                    modeSwitcher[deviceID].previousMode();
                else if (cState.Options)
                    modeSwitcher[deviceID].nextMode();
            }
        }

        public virtual void LogDebug(String Data)
        {
            if (Debug != null)
            {
                DebugEventArgs args = new DebugEventArgs(Data);
                OnDebug(this, args);
            }
        }

        public virtual void OnDebug(object sender, DebugEventArgs args)
        {
            if (Debug != null)
                Debug(this, args);
        }

        //sets the rumble adjusted with rumble boost
        public virtual void setRumble(byte heavyMotor, byte lightMotor, int deviceNum)
        {
            byte boost = Global.loadRumbleBoost(deviceNum);
            uint lightBoosted = ((uint)lightMotor * (uint)boost) / 100;
            if (lightBoosted > 255)
                lightBoosted = 255;
            uint heavyBoosted = ((uint)heavyMotor * (uint)boost) / 100;
            if (heavyBoosted > 255)
                heavyBoosted = 255;
            DS4Controllers[deviceNum].setRumble((byte)lightBoosted, (byte)heavyBoosted);
        }
    }
}
