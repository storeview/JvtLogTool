using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using System.IO.Compression;




namespace JvtLogTool
{
    public partial class MainWindow : Window
    {
        // 软件版本号
        private static string s_version = "V1.0.1";
        // 日志文件夹的顶层目录
        private static string s_softwareLogDir = Environment.CurrentDirectory + "\\Log";
        private static string s_softwareLogFile = s_softwareLogDir + "\\JvtLogTool.log";
        // 软件日志Writer
        private static StreamWriter s_softwareWriter = null;
        // 设备运行日志目录，由于不同设备有不同的日志，所以需要用不同的文件夹进行区分
        private static string s_deviceLogBaseDir = s_softwareLogDir + "\\Log__";
        // 运行日志tmp目录，临时存储一些setconsole打印
        private static string s_setconsoleLogTmpDir = s_softwareLogDir + "\\tmp";

        // 连接Telnet的线程，方便关闭Abort
        private Thread _telnetThread;
        // 运行日志的最终正式目录
        private string _deviceLogDir = "";
        // 运行日志的最终正式文件绝对路径名
        private string _setconsoleLogFile = null;
        
        //运行日志Writer
        StreamWriter SetconsoleWriter = null;


        /**
            主窗口构造函数，程序从此开始
        **/
        public MainWindow()
        {
            InitializeComponent();
            LoadAllDefaultParams();
            StartPrintSoftwareLog();
            StartAllSubThread();
        }


        /**
           加载所有参数
        **/
        void LoadAllDefaultParams()
        {
            ipBox.Text = Properties.Settings.Default.IP;
            usernameBox.Text = Properties.Settings.Default.UserName;
            passwordBox.Password = Properties.Settings.Default.PassWord;
            syslogCbx.IsChecked = Properties.Settings.Default.IsSyslog;
            setconsoleCbx.IsChecked = Properties.Settings.Default.IsSetconsole;
            screenshotCbx.IsChecked = Properties.Settings.Default.IsWeb;
            
            // 传输其他文件的选择框和文本框，由于安全原因，禁止使用，废弃
            //otherCbx.IsChecked = Properties.Settings.Default.IsOtherFile;
            //otherPath.Text = Properties.Settings.Default.OtherFilePath;
        }


        /**
            保存所有参数
        **/
        void SaveAllDefaultParams()
        {
            Properties.Settings.Default.IP = ipBox.Text;
            Properties.Settings.Default.UserName = usernameBox.Text;
            Properties.Settings.Default.PassWord = passwordBox.Password;
            Properties.Settings.Default.Save();

            Properties.Settings.Default.IsSyslog = (bool)syslogCbx.IsChecked;
            Properties.Settings.Default.IsSetconsole = (bool)setconsoleCbx.IsChecked;
            Properties.Settings.Default.IsWeb = (bool)screenshotCbx.IsChecked;

            // 传输其他文件的选择框和文本框，由于安全原因，禁止使用，废弃
            //Properties.Settings.Default.IsOtherFile = (bool)otherCbx.IsChecked;
            //Properties.Settings.Default.OtherFilePath = otherPath.Text;
            Properties.Settings.Default.Save();
        }


        /**
            开启日志
        **/
        void StartPrintSoftwareLog()
        {
            // 创建日志文件夹目录
            if (!Directory.Exists(s_softwareLogDir))
                Directory.CreateDirectory(s_softwareLogDir);
            // 创建日志文件
            if (!File.Exists(s_softwareLogFile))
                File.Create(s_softwareLogFile).Close();
            // 打开日志
            s_softwareWriter = File.AppendText(s_softwareLogFile);


            PrintSoftwareLog("【软件版本】"+ s_version);
            s_softwareWriter.WriteLine();
            s_softwareWriter.WriteLine("----------------------------------------");
            s_softwareWriter.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss  ") + "【打开软件】");
            s_softwareWriter.Flush();
        }


        /**
            开始所有的子线程
        **/
        void StartAllSubThread()
        {
            // 检查 telnet、tftp 是否在线，并更新页面状态UI
            new Task(() => CheckTelnetOnline()).Start();
            new Task(() => CheckTftpServerOnline()).Start();

            // --------------------> Telnet自动登录功能（暂时废弃） <--------------------
            /**
                string ip = ipBox.Text;
                string username = usernameBox.Text;
                string password = passwordBox.Password;
                _telnetThread = new Thread((() => StartTelnet(this, ip, username, password)));
                _telnetThread.IsBackground = true;
                _telnetThread.Start();
            **/

            // 创建 Tftp 服务器
            new Task((() => StartTftpServer(this))).Start();
        }



        /**
            开启 Telnet 服务器
        **/
        void StartTelnet(MainWindow mw, string ip, string username, string password)
        {

            //1.连接Telnet服务器
            PrintSoftwareLog("【Telnet】正在连接...");
            TelnetConnection tc = TelnetRunner.CreateTelnetClient(ip, username, password);
            PrintSoftwareLog(tc.RetMsg);

            SetTelnetConnectBtnStatus(true);

            //2.如果Telnet登录成功了，就开启setconsole日志
            if (tc.LoginStatus)
            {
                // 当前Telnet连接成功设备的日志目录

                var thisTime = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                _deviceLogDir =  $"{s_deviceLogBaseDir}{tc.Hostname}__{thisTime}";

                // 创建日志文件夹目录
                CreateDirectory(_deviceLogDir);
                // 创建临时存储日志文件的目录
                CreateDirectory(s_setconsoleLogTmpDir);

                // 先存储日志文件到tmp目录
                _setconsoleLogFile = s_setconsoleLogTmpDir + "\\" + tc.Hostname + "_setconsole.txt";
                // 创建日志文件
                CreateFile(_setconsoleLogFile);

                TelnetRunner.StartSetconsoleLog(tc, this);
            }

        }

        /**
            创建目录，如果目录已经存在则不再创建
        **/
        void CreateDirectory(string dir_path)
        {
            if (!Directory.Exists(dir_path))
                Directory.CreateDirectory(dir_path);
        }


        /**
            创建文件，如果目录下已经存在，则删除文件后，重新创建
        **/
        void CreateFile(string file_path)
        {
            if (File.Exists(file_path))
            {
                File.Delete(file_path);
            }
            File.Create(file_path).Close();
        }



        /**
            公共方法，同时输出日志到窗口和文件
        **/
        public void PrintSoftwareLog(string log)
        {
            PrintSoftwareLog(true, log);
        }


        /**
            公共方法，可选择是否输出日志的窗口
        **/
        public void PrintSoftwareLog(bool showInScreen, string log)
        {
            this.Dispatcher.Invoke(new Action(
                delegate
                {
                    if (showInScreen)
                    {
                        softwareLog.AppendText(DateTime.Now.ToString("HH:mm:ss  "));
                        softwareLog.AppendText(log);
                        softwareLog.AppendText("\r\n");
                        softwareLog.ScrollToEnd();
                    }
                    s_softwareWriter.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss  ") + log);
                    s_softwareWriter.Flush();
                })
            );
        }


        /**
            公共方法，用于输出 setconsole 日志到窗口中
        **/
        public void PrintSetconsoleLog(string log)
        {
            if (SetconsoleWriter == null)
                SetconsoleWriter = File.AppendText(_setconsoleLogFile);

            try
            {
                this.Dispatcher.Invoke(new Action(
                    delegate
                    {
                        setconsoleLogBox.AppendText(log);
                        setconsoleLogBox.AppendText("\r\n");
                        setconsoleLogBox.ScrollToEnd();

                        string cur_time = DateTime.Now.ToString("【yyyy-MM-dd HH:mm:ss】 ");
                        SetconsoleWriter.WriteLine(cur_time + log.Replace("\n", cur_time));
                        SetconsoleWriter.Flush();
                    })
                );
            }
            catch (Exception e)
            {
                if(SetconsoleWriter!= null)
                    SetconsoleWriter.Close();
                SetconsoleWriter = null;
            }
            
        }


        /**
            改变颜色
        **/
        void ChangeEllipseColor(Ellipse ellipse, SolidColorBrush scb)
        {
            ellipse.Dispatcher.Invoke(new Action(
                delegate
                {
                    ellipse.Fill = scb;
                })
            );
        }


        /**
            检查 Telnet 连接是否在线（通过标志位）
        **/
        void CheckTelnetOnline()
        {
            try
            {
                while (true)
                {
                    Thread.Sleep(1000);
                    if (TelnetRunner.Status)
                        ChangeEllipseColor(telnetStatus, Brushes.Chartreuse);
                    else
                        ChangeEllipseColor(telnetStatus, Brushes.Red);
                }
            }
            catch (Exception e) { }
        }


        /**
            检查 TFTP 服务器是否在线（通过标志位）
        **/
        void CheckTftpServerOnline()
        {
            try
            {
                while (true)
                {
                    Thread.Sleep(1000);
                    if (TftpServerRunner.Status)
                        ChangeEllipseColor(tftpServerStatus, Brushes.Chartreuse);
                    else
                        ChangeEllipseColor(tftpServerStatus, Brushes.Red);
                }
            }
            catch (Exception e) { }
        }


        /**
            开启 TFTP 服务器
            ！！！这一块的TFTP服务器使用的依赖库，不是很好用，需更换！！！
        **/
        void StartTftpServer(MainWindow mw)
        {
            TftpServerRunner.createTftpServer(mw);
        }


        /**
            登录Telnet服务器的线程
        **/
        void TelnetLoginBtn_Click(object sender, RoutedEventArgs e)
        {
            SaveAllDefaultParams();


            // 设置当前『登录』按钮不可点击
            SetTelnetConnectBtnStatus(false);

            // 中断之前的线程
            if (_telnetThread != null)
                _telnetThread.Abort();

            TelnetRunner.Status = false;

            // 将之前的一些对象置空，方便监测后重新创建
            if (SetconsoleWriter != null)
                SetconsoleWriter.Close();
            SetconsoleWriter = null;
            _telnetThread = null;

            var ip = ipBox.Text;
            var username = usernameBox.Text;
            var password = passwordBox.Password;
            setconsoleLogBox.Clear();
            //Thread.Sleep(1000);

            PrintSoftwareLog("【Telnet】尝试登录到设备："+ip);
            _telnetThread = new Thread((() => StartTelnet(this, ip, username, password)));
            _telnetThread.IsBackground = true;
            _telnetThread.Start();
        }


        void SetTelnetConnectBtnStatus(bool status)
        {

            this.Dispatcher.Invoke(new Action(
                delegate
                {
                    this.telnetReconnectBtn.IsEnabled = status;
                })
            );
        }


        /**
            开始打包
        **/
        void Package_Button_Click(object sender, RoutedEventArgs e)
        {
            if (_deviceLogDir.Equals("") || !Directory.Exists(_deviceLogDir))
            { 
                var nowTime = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                _deviceLogDir = $"{s_deviceLogBaseDir}{ipBox.Text}__{nowTime}";
                // 创建日志文件夹目录
                CreateDirectory(_deviceLogDir);
            }

            if (TftpServerRunner.Status)
            {
                var ip = ipBox.Text;
                var username = usernameBox.Text;
                var password = passwordBox.Password;
                bool setconsoleCbxStatus = (bool)setconsoleCbx.IsChecked;
                bool syslogCbxStatus = (bool)syslogCbx.IsChecked;
                //bool otherCbxStatus = (bool)otherCbx.IsChecked;
                //string otherCbxText = otherPath.Text;

                SaveAllDefaultParams();

                // 开启一个新的Task，并传入足够多的变量
                new Task(() => Package_Button_Click0(sender, e, ip, username, password, syslogCbxStatus, setconsoleCbxStatus, _deviceLogDir)).Start();
            }
        }


        /**
            打包文件的线程
        **/
        void Package_Button_Click0(object sender, RoutedEventArgs e, string ip, string username, string password, bool syslogCbxStatus, bool setconsoleCbxStatus,  string _deviceLogDir)
        {
            PrintSoftwareLog("【打包】开始打包...");

            if(!syslogCbxStatus && !setconsoleCbxStatus)
            {
                PrintSoftwareLog("【打包】没有选择需要打包的文件，请检查后重试");
                return ;
            }

            // 删除原来文件夹中的所有文件
            if (Directory.Exists(_deviceLogDir))
            {
                string[] files = Directory.GetFiles(_deviceLogDir);
                foreach (string file in files)
                {
                    File.Delete(file);
                }
            }

            // --------------------> 1 检查勾选框状态，依据勾选框处理文件，并且文件移动到device目录下 <--------------------
            if (syslogCbxStatus)
            //if (syslogCbxStatus || otherCbxStatus)
            {
                // --------------------> 1.1 TFTP传输文件 <--------------------

                // 建立一个Telnet连接，然后进行TFTP文件传输
                TelnetConnection tc = TelnetRunner.CreateTelnetClient(ip, username, password);
                PrintSoftwareLog(tc.RetMsg);
                if (tc.LoginStatus)
                {
                    // 传输 syslog 文件到本地
                    if (syslogCbxStatus)
                    {
                        string ret = TelnetRunner.TransferFileByTftp(tc, "/param/syslog", _deviceLogDir);
                        PrintSoftwareLog(false, ret);
                    }
                    // 传输其他文件到本地
                    /**
                    if (otherCbxStatus)
                    {
                        string[] lines = otherCbxText.Replace("\n", "#").Split('#');

                        foreach (string line in lines)
                        {
                            string filePath = line.Replace("；", ";").Split(';')[0];
                            if (filePath.Length != 0)
                            {
                                string ret = TelnetRunner.transferFileByTftp(tc, filePath, _deviceLogDir);
                                PrintSoftwareLog(false, ret);
                            }
                        }
                    }
                    **/
                }
                else
                {
                    PrintSoftwareLog("【打包】Telnet连接失败，无法通过Tftp传输设备中的文件，请检查网络后重试");
                }
            }

            if (setconsoleCbxStatus)
            {

                string source_setconsole_file = $"{s_setconsoleLogTmpDir}\\{ip}_setconsole.txt";
                if (!File.Exists(source_setconsole_file))
                {
                    PrintSoftwareLog($"【打包】文件不存在：{ip}_setconsole.txt");
                    return;
                }
                // --------------------> 1.2 移动setconsole文件 <--------------------
                string copy_target_path = $"{_deviceLogDir}\\{ip}_setconsole.txt";
                // 删除原来的文件
                if (File.Exists(copy_target_path))
                    File.Delete(copy_target_path);
                // 复制tmp目录下的setconsole文件
                File.Copy($"{s_setconsoleLogTmpDir}\\{ip}_setconsole.txt", copy_target_path);
            }

            // --------------------> 2. 检查目录下的文件是否齐全 <--------------------
            if (setconsoleCbxStatus)
            {
                if (File.Exists(_deviceLogDir + "\\" + ip + "_setconsole.txt"))
                    PrintSoftwareLog("【打包】setconsole文件打包成功！");
                else
                    PrintSoftwareLog("【打包】setconsole文件打包失败");
            }
            if (syslogCbxStatus)
            {
                if (File.Exists(_deviceLogDir + "\\" + ip + "_syslog.txt"))
                    PrintSoftwareLog("【打包】syslog文件打包成功！");
                else
                    PrintSoftwareLog("【打包】syslog文件打包失败");
            }
            /**
            if (otherCbxStatus)
            {
                string[] lines = otherCbxText.Replace("\n", "#").Split('#');
                foreach (string line in lines)
                {
                    string filePath = line.Replace("；", ";").Split(';')[0];
                    string filename2 = filePath.Split('/')[filePath.Split('/').Length - 1];
                    if (filename2.Length != 0)
                    {
                        if (File.Exists(_deviceLogDir + "\\" + ip + "_" + filename2+".txt"))
                            PrintSoftwareLog("【打包】" + filename2 + "文件打包成功！");
                        else
                            PrintSoftwareLog("【打包】" + filename2 + "文件打包失败");
                    }

                }
            }
            **/



            // --------------------> 3. 对整个文件夹进行打包 <--------------------
            string filename = _deviceLogDir.Split('\\')[_deviceLogDir.Split('\\').Length - 1];
            string dir = _deviceLogDir.Replace(filename, "");
            string zip_file_path = dir + "\\" + filename + ".zip";
            // --------------------> 打包成zip文件 <--------------------
            if (File.Exists(zip_file_path))
                File.Delete(zip_file_path);
            ZipFile.CreateFromDirectory(_deviceLogDir, zip_file_path);
            //PrintSoftwareLog("【打包】全部文件打包完成！");
            // --------------------> 传输到桌面 <--------------------
            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            desktopPath += "\\";
            string desktopFilePath = desktopPath + filename + ".zip";
            if (File.Exists(desktopFilePath))
                File.Delete(desktopFilePath);
            File.Copy(zip_file_path, desktopFilePath);
            //PrintSoftwareLog("【打包】压缩文件源文件地址：" + zip_file_path);
            PrintSoftwareLog("【打包】压缩文件已发送到桌面，文件名：" + filename);
        }



        void CheckUpdateBtn_Click(object sender, RoutedEventArgs e)
        {
            CheckUpdateWindow uWindow = new CheckUpdateWindow();
            uWindow.Show();
        }

        void SettingBtn_Click(object sender, RoutedEventArgs e)
        {
            PrintSoftwareLog("【偏好设置】功能暂未上线.");
        }

        /**
            输出软件版本号
        **/
        void VersionBtn_Click(object sender, RoutedEventArgs e)
        {
            PrintSoftwareLog("【软件版本】"+s_version);
        }
    }
}
