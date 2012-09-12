﻿using System;
using System.Collections.Generic;
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

namespace traqpaqWPF
{
    public enum PageName { WELCOME, RECORDS, UPLOAD, DATA };

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public Page[] pages;

        public MainWindow()
        {
            InitializeComponent();
            pages = new Page[] { new WelcomePage(this), new RecordTablePage(), new UploadPage(), new DataPage() };
            navigatePage(PageName.WELCOME);
        }

        public void navigatePage(PageName p)
        {
            frame1.Navigate(pages[(int)p]);
        }
    }
}
