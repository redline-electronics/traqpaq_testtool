using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using LibUsbDotNet;
using LibUsbDotNet.Main;
using LibUsbDotNet.Info;
using LibUsbDotNet.DeviceNotify;
using LibUsbDotNet.DeviceNotify.Info;

namespace traqpaqWPF
{

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public Page[] pages;
        public DispatcherTimer autoReadTimer;
        public TraqpaqDevice traqpaq;
        /// <summary>
        /// Used to detect when a device is connected, 
        /// if it is not connected already when the program is started.
        /// </summary>
        public IDeviceNotifier deviceNotifier;
        /// <summary>
        /// Use this list to save the previous pages that the user has visited.
        /// </summary>
        List<Page> backPageCache = new List<Page>();
        /// <summary>
        /// If the user hits the back button, save the page to this list
        /// </summary>
        List<Page> forwardPageCache = new List<Page>();

        public MainWindow()
        {
            InitializeComponent();

            autoReadTimer = new DispatcherTimer();
            autoReadTimer.Tick += new EventHandler(autoReadObjects);
            autoReadTimer.Interval = new TimeSpan(0, 0, 1);   // Hours, Minutes, Seconds

            
            
            // try to connect to device. Show status in status bar
            try
            {
                traqpaq = new TraqpaqDevice();
                // update status bar
                //traqpaq.myOTPreader.reqSerialNumber();
                statusBarItemTraqpaq.Content = "Device connected";
                oneTimeRead();
                autoReadTimer.Start();

            }
            catch (TraqPaqNotConnectedException)
            {
                // Device not found
                traqpaq = null;
                // update status bar
                statusBarItemTraqpaq.Content = "Device not found";
                // Set up event handler to wait for a usb device to connect to
                deviceNotifier = DeviceNotifier.OpenDeviceNotifier();
                deviceNotifier.OnDeviceNotify += new EventHandler<DeviceNotifyEventArgs>(deviceNotifier_OnDeviceNotify);
            }
        }

        /// <summary>
        /// Called whenever a usb device is plugged in or unplugged
        /// </summary>
        void deviceNotifier_OnDeviceNotify(object sender, DeviceNotifyEventArgs e)
        {
            // Detected a device, try to see if it is the traqpaq
            if (e.EventType == EventType.DeviceArrival)  // check for device arrival
            {
                //MessageBox.Show(e.Device.IdProduct.ToString() + "\n" + e.Device.IdVendor.ToString());
                if (traqpaq == null)
                {//TODO could also check for specifics with the e.Device..... properties
                    // try to connect again
                    try
                    {
                        traqpaq = new TraqpaqDevice();
                        statusBarItemTraqpaq.Content = "Device connected";
                        oneTimeRead();
                        autoReadTimer.Start();
                    }
                    catch (TraqPaqNotConnectedException) { }    // Silently fail
                }
            }
            else if(e.EventType == EventType.DeviceRemoveComplete)
            {
                //TODO use this to disconnect the device. The event handler would need to be created regardless
                //of wether or not the device is connected at first though.
                if (traqpaq.MyUSBDevice.IsOpen)
                {
                    traqpaq.MyUSBDevice.Close();
                    statusBarItemTraqpaq.Content = "Device not found";
                    autoReadTimer.Stop();
                }
            }
        }

        private void oneTimeRead( ){
            traqpaq.myOTPreader.reqSerialNumber();
            traqpaq.myOTPreader.reqTesterID();
            traqpaq.myOTPreader.reqHardwareVersion();
            traqpaq.myOTPreader.reqApplicationVersion();

            label_OTP_AppVersion.Content = traqpaq.myOTPreader.ApplicationVersion;
            label_OTP_HwVersion.Content = traqpaq.myOTPreader.HardwareVersion;
            label_OTP_SerialNumber.Content = traqpaq.myOTPreader.SerialNumber;
            label_OTP_TesterID.Content = traqpaq.myOTPreader.TesterID;
            label_OTP_Read.Text = BitConverter.ToString(traqpaq.myOTPreader.readOTP(64, 64)).Replace("-", " ");


            label_GPS_SerialNumber.Content = traqpaq.getGPS_SerialNo();
            label_GPS_PartNumber.Content = traqpaq.getGPS_PartNo();
            label_GPS_SWVersion.Content = traqpaq.getGPS_SW_Version();
            label_GPS_SWDate.Content = traqpaq.getGPS_SW_Date();

            progress_Flash_FreeSpace.Value = traqpaq.getFlashPercentUsed();
        }

        private void autoReadObjects(object sender, EventArgs e)
        {
            Position currentPosition;

            currentPosition = traqpaq.getGPS_CurrentPosition();

            label_GPS_Latitude.Content = currentPosition.latitude.ToString();
            label_GPS_Longitude.Content = currentPosition.longitude.ToString();
            label_GPS_Heading.Content = currentPosition.heading.ToString();
            
            traqpaq.battery.reqBatteryVoltage();
            label_Battery_Voltage.Content = traqpaq.battery.Voltage.ToString("0.00 V");

            traqpaq.battery.reqBatteryTemp();
            label_Battery_Temperature.Content = traqpaq.battery.Temperature.ToString("0.0 C");

            traqpaq.battery.reqBatteryInstCurrent();
            label_Battery_Instant_Current.Content = traqpaq.battery.CurrentInst.ToString("0.0 mA");

            traqpaq.battery.reqBatteryAccumCurrent();
            label_Battery_Accum_Current.Content = traqpaq.battery.CurrentAccum.ToString("0.0 mAh");
            progress_Battery_Meter.Value = (traqpaq.battery.CurrentAccum / 3200) * 100;  // TODO: Replace 3200 with battery capacity counts constant
        }

        private void button_GPS_View_Click(object sender, RoutedEventArgs e)
        {
            byte zoom = 14;         // zoom level (1-20)
            char type = 'm';        // "m" map, "k" satellite, "h" hybrid, "p" terrain, "e" GoogleEarth

            //mapView.Navigate("http://maps.google.com/maps?" + 
            //                "z=" + zoom.ToString() + 
            //                "&t=" + type.ToString() +
            //                "&q=loc:" + label_GPS_Latitude.Content + "+" + label_GPS_Longitude.Content
            //                );

            mapView.Navigate("http://maps.googleapis.com/maps/api/staticmap?center=" +
                            label_GPS_Latitude.Content + "," + label_GPS_Longitude.Content +
                            "&zoom=" + zoom.ToString() +
                            "&size=255x195&maptype=roadmap&sensor=false" +
                            "&markers=color:red%7Ccolor:red%7C%7C" + label_GPS_Latitude.Content + "," + label_GPS_Longitude.Content);
        }

        private void button_OTP_Serialize_Click(object sender, RoutedEventArgs e)
        {

        }
    }
}