﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Diagnostics;
using HidLibrary;
using System.Threading.Tasks;


using System.Linq;
using System.Text;
using System.IO;
using System.Collections;
namespace DS4Library
{
    public struct DS4Color
    {
        public byte red;
        public byte green;
        public byte blue;
    }

    public enum ConnectionType : byte { USB, BT };
    
    public class DS4Device
    {
        private const int BT_OUTPUT_REPORT_LENGTH = 78;
        private const int BT_INPUT_REPORT_LENGTH = 547;
        private HidDevice hDevice;
        private string Mac;
        private DS4State cState = new DS4State();
        private DS4State pState = new DS4State();
        private bool isTouchEnabled;
        private ConnectionType conType;
        private byte[] inputReport = new byte[64];
        private byte[] btInputReport = null;
        private byte[] outputReport = null;
        private DS4Touchpad touchpad = null;
        private byte lightRumble;
        private byte heavyRumble;
        private DS4Color ligtBarColor;
        private byte ledFlashOn, ledFlashOff;
        private bool isDirty = false;
        private Thread updaterThread = null;
        private int battery;
        public event EventHandler<EventArgs> Report = null;
        public event EventHandler<EventArgs> Removal = null;

        public HidDevice HidDevice { get { return hDevice; } }

        public string MacAddress { get { return Mac; } }

        public ConnectionType ConnectionType { get { return conType; } }

        public int Battery { get { return battery; } }

        public byte LightRumble
        {
            get { return lightRumble; }
            set
            {
                if (value == lightRumble) return;
                lightRumble = value;
                isDirty = true;
            }
        }

        public byte HeavyRumble
        {
            get { return heavyRumble; }
            set
            {
                if (value == heavyRumble) return;
                heavyRumble = value;
                isDirty = true;
            }
        }

        public bool TouchEnabled
        {
            get { return isTouchEnabled; }
            set { isTouchEnabled = value; }
        }

        public DS4Color LightBarColor
        {
            get { return ligtBarColor; }
            set
            {
                if (ligtBarColor.red != value.red || ligtBarColor.green != value.green || ligtBarColor.blue != value.blue)
                {
                    ligtBarColor = value;
                    isDirty = true;
                }
            }
        }

        public byte LightBarOnDuration
        {
            get { return ledFlashOn; }
            set
            {
                if (ledFlashOn != value)
                {
                    ledFlashOn = value;
                    isDirty = true;
                }
            }
        }
        
        public byte LightBarOffDuration
        {
            get { return ledFlashOff; }
            set
            {
                if (ledFlashOff != value)
                {
                    ledFlashOff = value;
                    isDirty = true;
                }
            }
        }

        public DS4Touchpad Touchpad { get { return touchpad; } }

        public DS4Device(HidDevice hidDevice)
        {            
            hDevice = hidDevice;
            hDevice.MonitorDeviceEvents = false;
            conType = hDevice.Capabilities.InputReportByteLength == 64 ? ConnectionType.USB : ConnectionType.BT;
            Mac = hDevice.readSerial();
            if (conType == ConnectionType.USB)
            {
                outputReport = new byte[hDevice.Capabilities.OutputReportByteLength];
            }
            else
            {
                btInputReport = new byte[BT_INPUT_REPORT_LENGTH];
                outputReport = new byte[BT_OUTPUT_REPORT_LENGTH];
            }
            touchpad = new DS4Touchpad();
        }

        public void StartUpdate()
        {
            if (updaterThread == null)
            {
                updaterThread = new Thread(updateCurrentState);
                updaterThread.Name = "DS4 Update thread :" + Mac;
                Console.WriteLine(updaterThread.Name + " has started");
                updaterThread.Start();
            }
            else
                Console.WriteLine("Thread already running for DS4: " + Mac);
        }

        public void StopUpdate()
        {
            if (updaterThread.ThreadState != System.Threading.ThreadState.Stopped || updaterThread.ThreadState != System.Threading.ThreadState.Aborted)
            {
                try
                {
                    updaterThread.Abort();
                    updaterThread = null;
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                }
            }
        }

        private void updateCurrentState()
        {
            while (true)
            {                 
                if (hDevice.IsTimedOut)
                {
                    if (Removal != null)
                        Removal(this, EventArgs.Empty);
                    return;
                }
                if (conType != ConnectionType.USB)
                    if (hDevice.ReadFile(btInputReport) == HidDevice.ReadStatus.Success)
                    {
                        Array.Copy(btInputReport, 2, inputReport, 0, 64);
                    }
                    else
                    {
                        if (Removal != null)
                            Removal(this, EventArgs.Empty);
                        return; 
                    }
                else if (hDevice.ReadFile(inputReport) != HidDevice.ReadStatus.Success) 
                {
                    if (Removal != null)
                        Removal(this, EventArgs.Empty);
                    return; 
                }

                if (cState == null)
                    cState = new DS4State();
                cState.LX = inputReport[1];
                cState.LY = inputReport[2];
                cState.RX = inputReport[3];
                cState.RY = inputReport[4];
                cState.L2 = inputReport[8];
                cState.R2 = inputReport[9];

                cState.Triangle = ((byte)inputReport[5] & (1 << 7)) != 0;
                cState.Circle = ((byte)inputReport[5] & (1 << 6)) != 0;
                cState.Cross = ((byte)inputReport[5] & (1 << 5)) != 0;
                cState.Square = ((byte)inputReport[5] & (1 << 4)) != 0;
                cState.DpadUp = ((byte)inputReport[5] & (1 << 3)) != 0;
                cState.DpadDown = ((byte)inputReport[5] & (1 << 2)) != 0;
                cState.DpadLeft = ((byte)inputReport[5] & (1 << 1)) != 0;
                cState.DpadRight = ((byte)inputReport[5] & (1 << 0)) != 0;

                //Convert dpad into individual On/Off bits instead of a clock representation
                byte dpad_state = 0;

                dpad_state = (byte)(
                ((cState.DpadRight ? 1 : 0) << 0) |
                ((cState.DpadLeft ? 1 : 0) << 1) |
	            ((cState.DpadDown ? 1 : 0) << 2) |
                ((cState.DpadUp ? 1 : 0) << 3));

                switch (dpad_state)
                {
                    case 0: cState.DpadUp = true; cState.DpadDown = false; cState.DpadLeft = false; cState.DpadRight = false; break;
                    case 1: cState.DpadUp = true; cState.DpadDown = false; cState.DpadLeft = false; cState.DpadRight = true; break;
                    case 2: cState.DpadUp = false; cState.DpadDown = false; cState.DpadLeft = false; cState.DpadRight = true; break;
                    case 3: cState.DpadUp = false; cState.DpadDown = true; cState.DpadLeft = false; cState.DpadRight = true; break;
                    case 4: cState.DpadUp = false; cState.DpadDown = true; cState.DpadLeft = false; cState.DpadRight = false; break;
                    case 5: cState.DpadUp = false; cState.DpadDown = true; cState.DpadLeft = true; cState.DpadRight = false; break;
                    case 6: cState.DpadUp = false; cState.DpadDown = false; cState.DpadLeft = true; cState.DpadRight = false; break;
                    case 7: cState.DpadUp = true; cState.DpadDown = false; cState.DpadLeft = true; cState.DpadRight = false; break;
                    case 8: cState.DpadUp = false; cState.DpadDown = false; cState.DpadLeft = false; cState.DpadRight = false; break;
                }

                cState.R3 = ((byte)inputReport[6] & (1 << 7)) != 0;
                cState.L3 = ((byte)inputReport[6] & (1 << 6)) != 0;
                cState.Options = ((byte)inputReport[6] & (1 << 5)) != 0;
                cState.Share = ((byte)inputReport[6] & (1 << 4)) != 0;
                cState.R1 = ((byte)inputReport[6] & (1 << 1)) != 0;
                cState.L1 = ((byte)inputReport[6] & (1 << 0)) != 0;

                cState.PS = ((byte)inputReport[7] & (1 << 0)) != 0;
                cState.TouchButton = (inputReport[7] & (1 << 2 - 1)) != 0;


                int charge = 0;
                if (conType == ConnectionType.USB)
                {
                    charge = (inputReport[30] - 16) * 10;
                    if (charge > 100)
                        charge = 100;
                }
                else
                {
                    charge = (inputReport[30] + 1) * 10;
                    if (charge > 100)
                        charge = 100;
                }

                cState.Battery = charge;
                battery = charge;

                if (isTouchEnabled)
                    touchpad.handleTouchpad(inputReport, cState.TouchButton);

                sendOutputReport();

                if ((!pState.PS || !pState.Options) && cState.PS && cState.Options)
                {
                    DisconnectBT();
                }
                if (pState == null)
                    pState = new DS4State();
                cState.Copy(pState);
                if (Report != null)
                    Report(this, EventArgs.Empty);
            }
        }

        private void sendOutputReport()
        {
            if (isDirty)
            {
                if (conType == ConnectionType.BT)
                {
                    outputReport[0] = 0x11;
                    outputReport[1] = 128;
                    outputReport[3] = 0xff;
                    outputReport[6] = lightRumble; //fast motor
                    outputReport[7] = heavyRumble; //slow motor
                    outputReport[8] = LightBarColor.red; //red
                    outputReport[9] = LightBarColor.green; //green
                    outputReport[10] = LightBarColor.blue; //blue
                    outputReport[11] = ledFlashOn; //flash on duration
                    outputReport[12] = ledFlashOff; //flash off duration

                    if (hDevice.WriteOutputReportViaControl(outputReport))
                        isDirty = false;
                }
                else
                {
                    outputReport[0] = 0x5;
                    outputReport[1] = 0xFF;
                    outputReport[4] = lightRumble; //fast motor
                    outputReport[5] = heavyRumble; //slow  motor
                    outputReport[6] = LightBarColor.red; //red
                    outputReport[7] = LightBarColor.green; //green
                    outputReport[8] = LightBarColor.blue; //blue
                    outputReport[9] = ledFlashOn; //flash on duration
                    outputReport[10] = ledFlashOff; //flash off duration
                    if (hDevice.WriteOutputReportViaInterrupt(outputReport, 8))
                    {
                        isDirty = false;
                    }
                }
            }
        }

        public bool DisconnectBT()
        {
            if (Mac != null)
            {
                Console.WriteLine("Trying to disonnect BT device");
                IntPtr btHandle = IntPtr.Zero;
                int IOCTL_BTH_DISCONNECT_DEVICE = 0x41000c;

                byte[] btAddr = new byte[8];
                string[] sbytes = Mac.Split(':');
                for (int i = 0; i < 6; i++)
                {
                    //parse hex byte in reverse order
                    btAddr[5 - i] = Convert.ToByte(sbytes[i], 16);
                }
                long lbtAddr = BitConverter.ToInt64(btAddr, 0);


                NativeMethods.BLUETOOTH_FIND_RADIO_PARAMS p = new NativeMethods.BLUETOOTH_FIND_RADIO_PARAMS();
                p.dwSize = Marshal.SizeOf(typeof(NativeMethods.BLUETOOTH_FIND_RADIO_PARAMS));
                IntPtr searchHandle = NativeMethods.BluetoothFindFirstRadio(ref p, ref btHandle);
                int bytesReturned = 0;
                bool success = false;
                while (!success && btHandle != IntPtr.Zero)
                {
                    success = NativeMethods.DeviceIoControl(btHandle, IOCTL_BTH_DISCONNECT_DEVICE, ref lbtAddr, 8, IntPtr.Zero, 0, ref bytesReturned, IntPtr.Zero);
                    NativeMethods.CloseHandle(btHandle);
                    if (!success)
                        if (!NativeMethods.BluetoothFindNextRadio(searchHandle, ref btHandle))
                            btHandle = IntPtr.Zero;

                }
                NativeMethods.BluetoothFindRadioClose(searchHandle);
                Console.WriteLine("Disconnect successul: " + success);
                if(success)
                    if (Removal != null)
                        Removal(this, EventArgs.Empty);
                return success;
            }
            return false;
        }

        public void setLightbarColor(byte red, byte green, byte blue)
        {
            if (red != ligtBarColor.red || green != ligtBarColor.green || blue != ligtBarColor.blue)
            {
                isDirty = true;
                ligtBarColor.red = red;
                ligtBarColor.green = green;
                ligtBarColor.blue = blue;
            }
        }

        public void setLightbarDuration(byte onDuration, byte offDuration)
        {
            LightBarOffDuration = offDuration;
            LightBarOnDuration = onDuration;
        }

        public void setRumble(byte lightMotor, byte heavyMotor)
        {
            LightRumble = lightMotor;
            HeavyRumble = heavyMotor;
        }

        public void enableTouchpad(bool enabled)
        {
            isTouchEnabled = enabled;
        }

        public DS4State getCurrentState()
        {
            if (cState != null)
                return cState.Clone();
            else return null;
        }

        public DS4State getPreviousState()
        {
            return pState.Clone();
        }

        override
        public String ToString()
        {
            return Mac;
        }
    }
}