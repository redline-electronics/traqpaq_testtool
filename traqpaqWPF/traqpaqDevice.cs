﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LibUsbDotNet;
using LibUsbDotNet.Main;
using LibUsbDotNet.Info;
using LibUsbDotNet.DeviceNotify;

namespace traqpaqWPF
{
    public class TraqpaqDevice
    {
        public UsbDevice MyUSBDevice;
        private static UsbDeviceFinder traqpaqDeviceFinder;
        UsbEndpointReader reader;
        UsbEndpointWriter writer;
        ErrorCode ec = ErrorCode.None;        
        public Battery battery;
        public SavedTrackReader trackReader;
        public List<TraqpaqDevice.SavedTrackReader.SavedTrack> trackList;
        RecordTableReader tableReader;
        public List<RecordTableReader.RecordTable> recordTableList;
        public RecordDataReader dataReader;
        public OTPreader myOTPreader;

        /// <summary>
        /// Class constructor for the traq|paq
        /// </summary>
        public TraqpaqDevice()
        {
            // find the device
            traqpaqDeviceFinder = new UsbDeviceFinder(Constants.VID, Constants.PID);

            // open the device
            this.MyUSBDevice = UsbDevice.OpenUsbDevice(traqpaqDeviceFinder);

            // If the device is open and ready
            if (MyUSBDevice == null) throw new TraqPaqNotConnectedException("Device not found");

            this.reader = MyUSBDevice.OpenEndpointReader(ReadEndpointID.Ep01);
            this.writer = MyUSBDevice.OpenEndpointWriter(WriteEndpointID.Ep02);

            // create the battery object
            this.battery = new Battery(this);

            // create the otp reader object
            this.myOTPreader = new OTPreader(this);

            // create a saved track reader to get a list of saved tracks
            this.trackReader = new SavedTrackReader(this);
            this.trackList = trackReader.trackList;

            // get the record table data
            tableReader = new RecordTableReader(this);
            tableReader.readRecordTable();
            this.recordTableList = tableReader.recordTable;
        }

        #region sendCommand
        /*************************************************
         * Methods for talking to the device             *
         * sendCommand() should not be called directly   *
         *************************************************/
        /// <summary>
        /// Send command with no command bytes in the writeBuffer
        /// </summary>
        /// <param name="cmd">USB command</param>
        /// <param name="readBuffer">Pre-allocated byte array</param>
        /// <returns>False if read or write error. True otherwise</returns>
        private bool sendCommand(USBcommand cmd, byte[] readBuffer)
        {
            int bytesRead, bytesWritten;
            byte[] writeBuffer = { (byte)cmd };

            this.ec = writer.Write(writeBuffer, Constants.TIMEOUT, out bytesWritten);
            if (this.ec != ErrorCode.None)
                return false;

            this.ec = reader.Read(readBuffer, Constants.TIMEOUT, out bytesRead);
            if (this.ec != ErrorCode.None)
                return false;
            return true;
        }

        /// <summary>
        /// Send the command to the device. Preferred method for adding command bytes
        /// </summary>
        /// <param name="cmd">USB command</param>
        /// <param name="readBuffer">Pre-allocated byte array</param>
        /// <param name="commandBytes">command bytes passed in order from byte0..byte1..byte(n)
        ///                            They are appended to the writeBuffer</param>
        /// <returns>False if read or write error. True otherwise.</returns>
        private bool sendCommand(USBcommand cmd, byte[] readBuffer, params byte[] commandBytes)
        {   
            int bytesRead, bytesWritten;
            byte[] writeBuffer = new byte[commandBytes.Length + 1];
            writeBuffer[0] = (byte)cmd;
            for (int i = 0; i < commandBytes.Length; i++)
            {
                writeBuffer[i+1] = commandBytes[i];
            }

            this.ec = writer.Write(writeBuffer, Constants.TIMEOUT, out bytesWritten);
            if (this.ec != ErrorCode.None)
                return false;

            this.ec = reader.Read(readBuffer, Constants.TIMEOUT, out bytesRead);
            if (this.ec != ErrorCode.None)
                return false;

            return true;
        }
        
        private bool readRecordData(byte[] readBuffer, ushort length, ushort index)
        {
            int bytesRead, bytesWritten;
            byte[] writeBuffer = { (byte)USBcommand.USB_CMD_READ_RECORDDATA, (byte)((length >> 8) & 0xFF),
                                     (byte)(length & 0xFF), (byte)((index >> 8) & 0xFF), (byte)(index & 0xFF) };

            this.ec = writer.Write(writeBuffer, Constants.TIMEOUT, out bytesWritten);
            if (this.ec != ErrorCode.None)
                return false;
            // now read the response 64 bytes at a time
            int i = 0;
            while (i < length)
            {
                this.ec = reader.Read(readBuffer, i, 64, Constants.TIMEOUT, out bytesRead);
                if (this.ec != ErrorCode.None)
                    return false;
                i += 64;
            }
            return true;
        }
        #endregion

        #region tFlashOTP
        /********************************************************
         * Methods for sending commands to the device           *
         * All these methods call the sendCommand() function.   *
         * These should be used to access the device.           *
         ********************************************************/
        public class OTPreader
        {
            private TraqpaqDevice parent;
            public string ApplicationVersion { get; set; }
            public string HardwareVersion { get; set; }
            public string SerialNumber { get; set; }
            public string TesterID { get; set; }

            public OTPreader(TraqpaqDevice parent) { this.parent = parent; }

            public bool reqApplicationVersion()
            {
                byte[] readBuff = new byte[2];
                if (parent.sendCommand(USBcommand.USB_CMD_REQ_APPL_VER, readBuff))
                {
                    this.ApplicationVersion = readBuff[0] + "." + readBuff[1];
                    return true;
                }
                else return false;
            }

            public bool reqHardwareVersion()
            {
                byte[] readBuff = new byte[1];
                if (parent.sendCommand(USBcommand.USB_CMD_REQ_HARDWARE_VER, readBuff))
                {
                    this.HardwareVersion = readBuff[0].ToString();
                    return true;
                }
                else return false;
            }

            public bool reqSerialNumber()
            {
                byte[] readBuff = new byte[Constants.OTP_SERIAL_LENGTH];
                if (parent.sendCommand(USBcommand.USB_CMD_REQ_SERIAL_NUMBER, readBuff))
                {
                    this.SerialNumber = Encoding.ASCII.GetString(readBuff);
                    return true;
                }
                else return false;
            }

            /// <summary>
            /// Request end-of-line Tester ID
            /// </summary>
            /// <returns></returns>
            public bool reqTesterID()
            {
                byte[] readBuff = new byte[1];
                if (parent.sendCommand(USBcommand.USB_CMD_REQ_TESTER_ID, readBuff))
                {
                    this.TesterID = readBuff[0].ToString();
                    return true;
                }
                else return false;
            }

            /// <summary>
            /// Read specified bytes from flash OTP
            /// </summary>
            /// <param name="length">Number of bytes to read</param>
            /// <param name="index">Byte index to start reading from</param>
            /// <returns></returns>
            public byte[] readOTP(byte length, byte index)
            {
                byte[] readBuff = new byte[length];
                if (parent.sendCommand(USBcommand.USB_CMD_READ_OTP, readBuff, length, index))
                {
                    //TODO figure out how to return this value
                    return readBuff;
                }
                return readBuff;
            }

            /// <summary>
            /// Write fixed data in flash OTP
            /// </summary>
            public void writeOTP()
            {
                byte[] readBuff = new byte[256]; // don't know how big to make the buffer
                if (parent.sendCommand(USBcommand.USB_CMD_WRITE_OTP, readBuff))
                {
                    //TODO learn more about this function
                }                
            }
        }
        #endregion

        public class Battery
        {
            byte[] VoltageRead = new byte[2];
            byte[] TemperatureRead = new byte[2];
            byte[] InstCurrentRead = new byte[2];
            byte[] AccumCurrentRead = new byte[2];
            byte[] SetChargeStateFlagRead = new byte[1];
            
            public double Voltage { get; set; }
            public double Temperature { get; set; }
            public double CurrentInst { get; set; }
            public double CurrentAccum { get; set; }
            public bool ChargeStateFlag { get; set; }

            private TraqpaqDevice traqpaq;

            public Battery(TraqpaqDevice parent) 
            {
                this.traqpaq = parent;
            }            

            /// <summary>
            /// Request current battery voltage
            /// </summary>
            /// <returns>True if request was successful, false otherwise</returns>
            public bool reqBatteryVoltage()
            {
                if (traqpaq.sendCommand(USBcommand.USB_CMD_REQ_BATTERY_VOLTAGE, VoltageRead))
                {   // convert to Volts
                    this.Voltage = BetterBitConverter.ToUInt16(VoltageRead, 0) * Constants.BATT_VOLTAGE_FACTOR;                    
                    return true;
                }
                else return false;
            }

            /// <summary>
            /// Request battery temperature
            /// </summary>
            /// <returns>True if request was successful, false otherwise</returns>
            public bool reqBatteryTemp()
            {
                if (traqpaq.sendCommand(USBcommand.USB_CMD_REQ_BATTERY_TEMPERATURE, TemperatureRead))
                {
                    this.Temperature = BetterBitConverter.ToUInt16(TemperatureRead, 0) * Constants.BATT_TEMP_FACTOR; // measured in °C
                    return true;
                }
                else return false;
            }

            /// <summary>
            /// Request battery instantaneous current draw
            /// </summary>
            /// <returns>True if request was successful, false otherwise</returns>
            public bool reqBatteryInstCurrent()
            {
                if (traqpaq.sendCommand(USBcommand.USB_CMD_REQ_BATTERY_INSTANT, InstCurrentRead))
                {
                    this.CurrentInst = BetterBitConverter.ToInt16(InstCurrentRead, 0) * Constants.BATT_INST_CURRENT_FACTOR;
                    return true;
                }
                else return false;
            }

            /// <summary>
            /// Request battery accumulated current draw
            /// </summary>
            /// <returns>True if request was successful, false otherwise</returns>
            public bool reqBatteryAccumCurrent()
            {
                if (traqpaq.sendCommand(USBcommand.USB_CMD_REQ_BATTERY_ACCUM, AccumCurrentRead))
                {
                    this.CurrentAccum = BetterBitConverter.ToUInt16(AccumCurrentRead, 0) * Constants.BATT_ACCUM_CURRENT_FACTOR;
                    return true;
                }
                else return false;
            }

            /// <summary>
            /// Set accumulated battery current to full-charge state
            /// </summary>
            /// <returns>True if request was successful, false otherwise</returns>
            public bool setBatteryFullChargeState()
            {
                if (traqpaq.sendCommand(USBcommand.USB_CMD_REQ_BATTERY_UPDATE, SetChargeStateFlagRead))
                {
                    this.ChargeStateFlag = (SetChargeStateFlagRead[0] > 0);     //true if success
                    return true; 
                }
                else return false;
            }
        }


        public class SavedTrackReader
        {          
            public List<SavedTrack> trackList = new List<SavedTrack>();
            private TraqpaqDevice traqpaq;

            public SavedTrackReader(TraqpaqDevice parent)
            {
                this.traqpaq = parent;
                // get all the saved tracks and add them to the list
                getAllTracks();
            }

            /// <summary>
            /// Get all the saved tracks on the device and add them to the trackList
            /// </summary>
            private void getAllTracks()
            {
                bool isEmpty = false;
                SavedTrack track;
                ushort index = 0;

                while (!isEmpty)
                {   // keep reading tracks until one is empty
                    track = new SavedTrack(this, index);
                    if (track.readTrack())
                    {
                        if (!track.isEmpty)
                            trackList.Add(track);   // don't add the track if it is empty
                        isEmpty = track.isEmpty;
                        index++;
                    }
                    else isEmpty = false;   // track read failed
                }
            }

            //TODO test this function
            public bool writeSavedTracks()
            {
                byte[] readBuffer = new byte[1];
                if (traqpaq.sendCommand(USBcommand.USB_CMD_WRITE_SAVED_TRACKS, readBuffer))
                {
                    return readBuffer[0] > 0;
                }
                else return false;
            }

            public class SavedTrack
            {
                private byte[] trackReadBuff = new byte[Constants.TRACKLIST_SIZE];
                SavedTrackReader parent;
                ushort index;
                public string trackName { get; set; }
                public double Longitute { get; set; }
                public double Latitude { get; set; }
                public ushort Heading { get; set; }
                public bool isEmpty { get; set; }

                public SavedTrack(SavedTrackReader parent, ushort index)
                {
                    this.parent = parent;
                    this.index = index;
                }
                /// <summary>
                /// Read saved track data from the device
                /// </summary>
                /// <returns>True if successful, false if there was an error</returns>
                public bool readTrack()
                {
                    byte[] byteIndex = BetterBitConverter.GetBytes(index);
                    if (parent.traqpaq.sendCommand(USBcommand.USB_CMD_READ_SAVED_TRACKS, trackReadBuff, byteIndex[0], byteIndex[1]))
                    {
                        this.trackName = Encoding.ASCII.GetString(trackReadBuff, 0, Constants.TRACKLIST_NAME_STRLEN);
                        this.Longitute = BetterBitConverter.ToInt32(trackReadBuff, Constants.TRACKLIST_LONGITUDE) / Constants.LATITUDE_LONGITUDE_COORD;
                        this.Latitude = BetterBitConverter.ToInt32(trackReadBuff, Constants.TRACKLIST_LATITUDE) / Constants.LATITUDE_LONGITUDE_COORD;
                        this.Heading = BetterBitConverter.ToUInt16(trackReadBuff, Constants.TRACKLIST_COURSE);
                        this.isEmpty = (trackReadBuff[Constants.TRACKLIST_ISEMPTY] == 0xFF);  // true if empty
                        return true;
                    }
                    else return false;
                }
            }
        }


        public class RecordTableReader
        {            
            TraqpaqDevice traqpaq;
            public List<RecordTable> recordTable = new List<RecordTable>();

            public RecordTableReader(TraqpaqDevice parent)
            {
                this.traqpaq = parent;
            }

            /// <summary>
            /// An individual record table
            /// 16 byte struct is returned in the readBuffer.
            /// The readRecordTable function deciphers the bytes.
            /// </summary>
            public class RecordTable
            {
                RecordTableReader parent;
                byte[] readBuffer = new byte[Constants.RECORD_TABLE_SIZE];
                public bool RecordEmpty { get; set; }
                public byte TrackID { get; set; }
                public uint DateStamp { get; set; }
                public uint StartAddress { get; set; }
                public uint EndAddress { get; set; }
                public uint StartPage { get; set; }
                public uint EndPage { get; set; }

                public RecordTable(RecordTableReader parent)
                {
                    this.parent = parent;
                }

                public bool readRecordTable(byte index)
                {
                    if (parent.traqpaq.sendCommand(USBcommand.USB_CMD_READ_RECORDTABLE, readBuffer, (byte)Constants.RECORD_TABLE_SIZE, index))
                    {   
                        this.RecordEmpty = readBuffer[Constants.RECORD_EMPTY] == 0xFF;   // true if empty
                        this.TrackID = readBuffer[Constants.RECORD_TRACK_ID];
                        this.DateStamp = BetterBitConverter.ToUInt32(readBuffer, Constants.RECORD_DATESTAMP);
                        this.StartAddress = BetterBitConverter.ToUInt32(readBuffer, Constants.RECORD_START_ADDRESS);
                        this.EndAddress = BetterBitConverter.ToUInt32(readBuffer, Constants.RECORD_END_ADDRESS);
                        this.StartPage = (StartAddress - Constants.ADDR_RECORD_DATA_START) / Constants.MEMORY_PAGE_SIZE;
                        this.EndPage = (EndAddress - Constants.ADDR_RECORD_DATA_START) / Constants.MEMORY_PAGE_SIZE;
                        return true;
                    }
                    else return false;
                }
            }

            /// <summary>
            /// Populate the record table list. Keep reading records until one is empty
            /// </summary>
            /// <returns></returns>
            public bool readRecordTable()
            {   //TODO test this function
                bool isEmpty = false;
                byte index = 0;
                RecordTable table;

                while (!isEmpty)
                {
                    table = new RecordTable(this);
                    if (table.readRecordTable(index))
                    {
                        isEmpty = table.RecordEmpty;
                        if (!isEmpty)   // only add the table if it is not empty
                            this.recordTable.Add(table);
                        index++;
                    }
                    else return false;
                }
                return true;
            }
        }


        /// <summary>
        /// Reads the record data from the device using the start address and
        /// end address for a record table. This class must be created with a 
        /// record table object.
        /// </summary>
        public class RecordDataReader
        {
            RecordTableReader.RecordTable recordTable;
            TraqpaqDevice traqpaq;
            //public List<RecordDataPage> recordData = new List<RecordDataPage>();
            public RecordDataPage[] recordDataPages;
            uint numPages;

            public RecordDataReader(TraqpaqDevice parent, RecordTableReader.RecordTable recordTable)
            {
                this.traqpaq = parent;
                this.recordTable = recordTable;
                // allocate the recordData array, 256 bytes per page
                this.numPages = (recordTable.EndAddress - recordTable.StartAddress) / Constants.MEMORY_PAGE_SIZE;
                this.recordDataPages = new RecordDataPage[numPages];
            }

            public class RecordDataPage
            {
                RecordDataReader parent;
                byte[] dataPage = new byte[Constants.MEMORY_PAGE_SIZE];
                public double utc { get; set; }
                public double hdop { get; set; }
                public byte GPSmode { get; set; }
                public byte Satellites { get; set; }
                public tRecordData[] RecordData { get; set; }                

                public RecordDataPage(RecordDataReader parent)
                {
                    this.parent = parent;
                }

                public struct tRecordData
                {
                    public double Latitude { get; set; }
                    public double Longitude { get; set; }
                    public bool lapDetected { get; set; }
                    public double Altitude { get; set; }
                    public double Speed { get; set; }
                    public double Heading { get; set; }
                }

                public bool readRecordDataPage(int index)
                {
                    ushort length = 256;
                    if (parent.traqpaq.readRecordData(dataPage, length, (ushort)index))
                    {
                        // extract the data from the dataPage byte array
                        this.utc = BetterBitConverter.ToUInt32(dataPage, Constants.RECORD_DATA_UTC) / Constants.UTC_FACTOR;
                        this.hdop = BetterBitConverter.ToUInt16(dataPage, Constants.RECORD_DATA_HDOP) / Constants.HDOP_FACTOR;
                        this.GPSmode = dataPage[Constants.RECORD_DATA_MODE];
                        this.Satellites = dataPage[Constants.RECORD_DATA_SATELLITES];
                        // set the array of tRecordData structs
                        this.RecordData = new tRecordData[Constants.RECORD_DATA_PER_PAGE];
                        for (int i = 0; i < Constants.RECORD_DATA_PER_PAGE; i++)
                        {
                            this.RecordData[i].Latitude = BetterBitConverter.ToInt32(dataPage, Constants.RECORD_DATA_LATITUDE + (i * Constants.RECORD_DATA_SIZE) + 16) / Constants.LATITUDE_LONGITUDE_COORD;
                            this.RecordData[i].Longitude = BetterBitConverter.ToInt32(dataPage, Constants.RECORD_DATA_LONGITUDE + (i * Constants.RECORD_DATA_SIZE) + 16) / Constants.LATITUDE_LONGITUDE_COORD;
                            this.RecordData[i].lapDetected = dataPage[Constants.RECORD_DATA_LAP_DETECTED + (i * Constants.RECORD_DATA_SIZE) + 16] == 0x01;   // 0x00 means lap not detected. True if lap is detected
                            this.RecordData[i].Altitude = BetterBitConverter.ToUInt16(dataPage, Constants.RECORD_DATA_ALTITUDE + (i * Constants.RECORD_DATA_SIZE) + 16) / Constants.ALTITUDE_FACTOR;
                            this.RecordData[i].Speed = BetterBitConverter.ToUInt16(dataPage, Constants.RECORD_DATA_SPEED + (i * Constants.RECORD_DATA_SIZE) + 16) * Constants.SPEED_FACTOR;
                            this.RecordData[i].Heading = BetterBitConverter.ToUInt16(dataPage, Constants.RECORD_DATA_COURSE + (i * Constants.RECORD_DATA_SIZE) + 16) / Constants.COURSE_FACTOR;
                        }
                        return true;
                    }
                    else return false;
                }
            }

            /// <summary>
            /// Read all the pages for a given record
            /// </summary>
            /// <returns>True if successful, false otherwise</returns>
            public bool readRecordData()
            {
                for (int i = 0; i < numPages; i++)
                {
                    this.recordDataPages[i] = new RecordDataPage(this);
                    this.recordDataPages[i].readRecordDataPage((int)this.recordTable.StartPage + i);
                }
                return true;
            }

            /// <summary>
            /// Erase all recorded data.
            /// WARNING: Do not call this function until able to record new data to the device!
            /// </summary>
            /// <returns>True if successful, false otherwise</returns>
            public bool eraseAllRecordData()
            {
                byte[] readBuffer = new byte[1];
                if (traqpaq.sendCommand(USBcommand.USB_CMD_ERASE_RECORDDATA, readBuffer))
                    return readBuffer[0] > 0;   // true if successful
                return false;
            }
        }

        /// <summary>
        /// Write the default user preferences to flash.
        /// </summary>
        /// <returns>True if successful, false otherwise</returns>
        public bool writeDefaultPrefs()
        {
            byte[] readBuff = new byte[1];
            if (sendCommand(USBcommand.USB_CMD_WRITE_USERPREFS, readBuff))
                return readBuff[0] > 0x00;  //TODO verify that this is the correct value for success
            else return false;
        }

        #region Debug functions
        /// <summary>
        /// Erase flash sector
        /// </summary>
        /// <param name="index">Index of sector to erase</param>
        /// <returns>True if successful, false otherwise</returns>
        public bool eraseFlash(byte index)
        {
            byte[] readBuff = new byte[1];
            if (sendCommand(USBcommand.USB_DBG_DF_SECTOR_ERASE, readBuff, index))
                return readBuff[0] > 0x00;  //TODO verify that this is the correct value for success
            else return false;
        }

        /// <summary>
        /// Check if flash is busy with at read or write operation
        /// </summary>
        /// <returns>True if busy, false otherwise</returns>
        public bool isFlashBusy()
        {
            byte[] readBuff = new byte[1];
            if (sendCommand(USBcommand.USB_DBG_DF_BUSY, readBuff))
                return readBuff[0] > 0x00;  //TODO verify that this is the correct value for busy
            else return false;
        }

        /// <summary>
        /// Erase entire flash
        /// </summary>
        /// <returns>True if success, false otherwise</returns>
        public bool eraseChip()
        {
            byte[] readBuff = new byte[1];
            if (sendCommand(USBcommand.USB_DBG_DF_CHIP_ERASE, readBuff))
                return readBuff[0] > 0x00;  //TODO verify that this is the correct value for success
            else return false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public bool isFlashFull()
        {
            byte[] readBuff = new byte[1];
            if (sendCommand(USBcommand.USB_DBG_DF_IS_FLASH_FULL, readBuff))
                return readBuff[0] > 0x00;  //TODO verify that this is the correct value for success
            else return false;
        }

        /// <summary>
        /// Get percentage of used space in flash
        /// </summary>
        /// <returns>Percentage of used space, or -1 if request fails</returns>
        public int getFlashPercentUsed()
        {
            byte[] readBuff = new byte[1];
            if (sendCommand(USBcommand.USB_DBG_DF_USED_SPACE, readBuff))
                return readBuff[0];  //TODO verify that this is the correct value for success
            else return -1;
        }

        /// <summary>
        /// Read the last received GPS latitude
        /// </summary>
        /// <returns>The last GPS latitude as unsigned integer, or 0 if request fails</returns>
        public Position getGPS_CurrentPosition()
        {
            byte[] readBuff = new byte[12];
            if (sendCommand(USBcommand.USB_DBG_GPS_CURRENT_POSITION, readBuff))
            {
                return new Position(readBuff);
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Gets the GPS Serial Number
        /// </summary>
        /// <returns>The SN as an unsigned int, 0 if the command fails or if the SN is invalid</returns>
        public uint getGPS_SerialNo()
        {
            byte[] readBuff = new byte[5];
            if (sendCommand(USBcommand.USB_DBG_GPS_INFO_SN, readBuff))
            {
                // check if SN is valid
                if (readBuff[0] > 0)
                {
                    return BetterBitConverter.ToUInt32(readBuff, 1);
                }
                else return 0;
            }
            else return 0;
        }

        /// <summary>
        /// Gets the GPS receiver part number
        /// </summary>
        /// <returns>ASCII string containing the part number, null if the command fails, or empty string if the part number is invalid</returns>
        public string getGPS_PartNo()
        {
            byte[] readBuff = new byte[9];
            if (sendCommand(USBcommand.USB_DBG_GPS_INFO_PN, readBuff))
            {
                // check if PN is valid
                if (readBuff[0] > 0)    //TODO check if this is the right test
                {
                    return Encoding.ASCII.GetString(readBuff, 1, Constants.GPS_INFO_PART_NUMBER_SIZE);
                }
                else return "";
            }
            else return null;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public string getGPS_SW_Version()
        {
            byte[] readBuff = new byte[10];
            if (sendCommand(USBcommand.USB_DBG_GPS_INFO_SW_VER, readBuff))
            {
                // check if SW Version is valid
                if (readBuff[0] > 0)    //TODO check if this is the right test
                {
                    return Encoding.ASCII.GetString(readBuff, 1, Constants.GPS_INFO_SW_VERSION_SIZE);
                }
                else return "";
            }
            else return null;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public string getGPS_SW_Date()
        {
            byte[] readBuff = new byte[10];
            if (sendCommand(USBcommand.USB_DBG_GPS_INFO_SW_DATE, readBuff))
            {
                // check if SW Date is valid
                if (readBuff[0] > 0)    //TODO check if this is the right test
                {
                    return Encoding.ASCII.GetString(readBuff, 1, Constants.GPS_INFO_SW_DATE_SIZE);
                }
                else return "";
            }
            else return null;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public bool startGPS_Recording()
        {            
            return sendCommand(USBcommand.USB_DBG_START_RECORDING, new byte[1]);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public bool stopGPS_Recording()
        {
            return sendCommand(USBcommand.USB_DBG_STOP_RECORDING, new byte[1]);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public bool getGPS_RecordingStatus()
        {
            byte[] readBuff = new byte[1];
            if (sendCommand(USBcommand.USB_DBG_RECORDING_STATUS, readBuff))
            {
                return readBuff[0] > 0;
            }
            else return false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public tAccelStatus getAccelStatus()
        {
            byte[] readBuff = new byte[1];
            if (sendCommand(USBcommand.USB_DBG_ACCEL_GET_STATUS, readBuff))
            {
                return (tAccelStatus)readBuff[0];
            }
            else return tAccelStatus.UNKNOWN;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public AccelerometerFiltered getFilteredAccel()
        {
            byte[] readBuff = new byte[6];
            if (sendCommand(USBcommand.USB_DBG_ACCEL_GET_FILT_DATA, readBuff))
            {
                return new AccelerometerFiltered(readBuff);
            }
            else return null;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public AccelerometerNormalized getNormalAccel()
        {
            byte[] readBuff = new byte[6];
            if (sendCommand(USBcommand.USB_DBG_ACCEL_GET_NORM_DATA, readBuff))
            {
                return new AccelerometerNormalized(readBuff);
            }
            else return null;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public AccelerometerSelfTest getSelfTestAccel()
        {
            byte[] readBuff = new byte[6];
            if (sendCommand(USBcommand.USB_DBG_ACCEL_GET_ST_DATA, readBuff))
            {
                return new AccelerometerSelfTest(readBuff);
            }
            else return null;
        }        
        #endregion
    }

    public class Position
    {
        public double latitude;
        public double longitude;
        public double heading;

        public Position(byte[] readBuff)
        {
            latitude = BetterBitConverter.ToInt32(readBuff, 0) / Constants.LATITUDE_LONGITUDE_COORD;
            longitude = BetterBitConverter.ToInt32(readBuff, 4) / Constants.LATITUDE_LONGITUDE_COORD;
            heading = BetterBitConverter.ToInt16(readBuff, 8) / Constants.COURSE_FACTOR;
        }
    }

    public class AccelerometerFiltered
    {
        public short Filtered_X { get; set; }
        public short Filtered_Y { get; set; }
        public short Filtered_Z { get; set; }

        public AccelerometerFiltered(byte[] readBuff)
        {
            Filtered_X = BetterBitConverter.ToInt16(readBuff, 0);
            Filtered_Y = BetterBitConverter.ToInt16(readBuff, 2);
            Filtered_Z = BetterBitConverter.ToInt16(readBuff, 4);
        }
    }
    public class AccelerometerNormalized
    {
        public short Normalized_X { get; set; }
        public short Normalized_Y { get; set; }
        public short Normalized_Z { get; set; }

        public AccelerometerNormalized(byte[] readBuff)
        {
            Normalized_X = BetterBitConverter.ToInt16(readBuff, 0);
            Normalized_Y = BetterBitConverter.ToInt16(readBuff, 2);
            Normalized_Z = BetterBitConverter.ToInt16(readBuff, 4);
        }
    }
    public class AccelerometerSelfTest
    {
        public short SelfTest_X { get; set; }
        public short SelfTest_Y { get; set; }
        public short SelfTest_Z { get; set; }

        public AccelerometerSelfTest(byte[] readBuff)
        {
            SelfTest_X = BetterBitConverter.ToInt16(readBuff, 0);
            SelfTest_Y = BetterBitConverter.ToInt16(readBuff, 2);
            SelfTest_Z = BetterBitConverter.ToInt16(readBuff, 4);
        }
    }
}
