using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.SqlClient;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
using System.Reflection;
using System.Globalization;
//using System.Text.RegularExpressions;

namespace ImportPrecip
{
    public partial class FrmMain : Form
    {
        //
        // Declare publics
        //
        public SqlConnection jbaConnection = new SqlConnection();
        public string SqlConnectionStringServer = 
            Properties.Settings.Default.SqlConnectionStringServer; // stored in app.config
        public string JbaDatabaseName = "jbaMaster"; // Database to use/create on server
        public string precipFile = ""; // Will hold path and filename of user selected import file

        public FrmMain()
        {
            InitializeComponent();
            if (SqlConnectionStringServer.IndexOf(";") == -1)
            {
                SqlConnectionStringServer += ";";
            }
        }

        // User has elected to start the import.
        // 1. Check that the database (JbaMaster) exists - If not create it from the script resource "createJbaMaster"
        // 2. Check that the table (PrecipData) exists - If not create it from the script resource "createPrecipData"
        // 3. Import the selected file
        private void btnConnect_Click(object sender, EventArgs e)
        {
            // Does our Database exist ?
            UpdateProgress("> Checking for existance of " + JbaDatabaseName + "...");
            if (!DatabaseExists(JbaDatabaseName))
            {
                // Database does not exist. Attempt to create it.
                UpdateProgress("> " + JbaDatabaseName + " does not exist");
                UpdateProgress("> Creating " + JbaDatabaseName + "...");
                ExecuteSqlScript("master", "createJbaMaster.sql");
                // Give the server time to acknowledge the database
                for (int i = 0; i < 1500; i++)
                {
                    if (DatabaseExists(JbaDatabaseName))
                    {
                        break;
                    }
                }
                if (!DatabaseExists(JbaDatabaseName))
                {
                    UpdateProgress("> " + JbaDatabaseName + " could not be created!!!");
                    return;
                }
                UpdateProgress("> " + JbaDatabaseName + " successfully created.");
            }
            else
            {
                UpdateProgress("> " + JbaDatabaseName + " Exists.");
            }
            // Database exists on the server
            // Check to see if our data table exists
            UpdateProgress("> Checking for existance of TABLE PrecipData...");
            if (!DatabaseTableExists("PrecipData"))
            {
                // PrecipData table does not exist on the server.
                // Attempt to create it.
                UpdateProgress("> PrecipData does not exist.");
                UpdateProgress("> Attempting to create table PrecipData...");
                ExecuteSqlScript(JbaDatabaseName, "createPrecipData.sql");

                // Give the server time to acknowledge the table
                for (int i = 0; i < 1500; i++)
                {
                    if (DatabaseTableExists("PrecipData"))
                    {
                        break;
                    }
                }
                if (!DatabaseTableExists("PrecipData"))
                {
                    UpdateProgress("> PrecipData table could not be created!!!");
                    return;
                }
                UpdateProgress("> PrecipData table successfully created.");
            }
            else
            {
                UpdateProgress("> PrecipData table Exists.");
            }
            if (ImportData())
            {
                UpdateProgress("> COMPLETED SUCCESSFULLY");
            }
            else
            {
                UpdateProgress("READ FAILED OR ABORTED BY USER!");
            }
        }
        
        // Main Import Routine.
        // Import the contents of named by precipFile
        private bool ImportData()
        {
            bool succeeded = false;
            FileStream fs = File.Open(precipFile, FileMode.Open, FileAccess.Read);
            StreamReader sr = new StreamReader(fs);
            string insertStatement = "INSERT INTO PrecipData (Xref, Yref, Date, Value) " +
                                    "VALUES ({0}, {1}, '{2}', {3})";
            try
            {
                UpdateProgress("> Reading/Checking Raw Data...");
                string text = sr.ReadToEnd(); // read all import data
                // data sanity test
                int temp = text.IndexOf("Grid-ref=");
                if (temp == -1)
                {
                    MessageBox.Show("Invalid or No data found in file. Aborting", "Warning",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return false;
                }
                UpdateProgress("> Data appears to be valid.");
                // !! FOLLOWING LINES REMOVED AS DATA CAN BE COMPROMISED !!

                /* remove redundant spacing i.e. multiple spaces become single spaces
                RegexOptions options = RegexOptions.None;
                Regex regex = new Regex("[ ]{2,}", options);
                text = regex.Replace(text, " ");*/

                // convert our bulk text into list items as these are easier to handle
                List<string> lines = new List<string>(
                        text.Split(new string[] { "\n" },
                        StringSplitOptions.RemoveEmptyEntries));
                text = null;
                // Set Database connection
                jbaConnection = new SqlConnection("Server="+ SqlConnectionStringServer +
                                        "Trusted_Connection=yes;" +
                                        "Database=" + JbaDatabaseName + ";" +
                                        //"User Instance=true;" +
                                        "Connection timeout=5");
                // Define command structure
                SqlCommand command = new SqlCommand();
                command.Connection = jbaConnection;
                command.CommandType = CommandType.Text;
                command.Connection.Open();

                // Check to see if data already exists in the file
                command.CommandText = "SELECT COUNT(*) FROM PrecipData";
                int recCount = (int)command.ExecuteScalar();
                if (recCount > 0)
                {
                    // table is already populated - Dous user want to overwrite and continue?
                    DialogResult result = MessageBox.Show("Data Already Exists in PrecipData Table\r\n\r\nDo you wish to overwrite it", "Warning",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);
                    if (result == DialogResult.No)
                    {
                        return false; // User has aborted the import
                    }
                    else
                    {
                        // Clear pre-existing table data
                        command.CommandText = "DELETE FROM PrecipData";
                        command.ExecuteNonQuery();
                        UpdateProgress("> PrecipData has been cleared.");
                        UpdateProgress("> ...Import continuing.");
                    }
                }
                UpdateProgress("> Importing Data...");
                string lineData = "";
                bool headersDone = false;
                int xRef = 0;
                int yRef = 0;
                int startYear = -1;
                int precipYear = -1;
                pbProgress.Maximum = lines.Count();
                pbProgress.Value = 0;
                for (int lineNo = 0; lineNo < lines.Count(); lineNo++)
                {
                    lineData = lines[lineNo].ToString();
                    if (lineData.Length > 9 && lineData.Substring(0, 9).ToUpper() == "GRID-REF=")
                    {
                        // we have the START of a data section
                        // We need to extract new x and y Ref's and reset our Year (precipYear)
                        lineData = lineData.Substring(9, lineData.Length - 9).Trim();
                        string[] splitString = lineData.Split(',');
                        xRef = Convert.ToInt16(splitString[0].Trim());
                        yRef = Convert.ToInt16(splitString[1].Trim());
                        precipYear = startYear; // initialise the year of the new data section
                        headersDone = true; // A data section "Grid-Ref=..." indicates
                                            // that we have done with all the header text
                    }
                    else if (headersDone)
                    {
                        // We have a data line
                        string[] monthValues = lineData.Split(' ');
                        int value = 0;
                        for (int month = 0; month < 12; month++)
                        {
                            try
                            {
                                string checkValue = lineData.Substring(month * 5, 5);
                                value = Convert.ToInt16(checkValue);
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show("An error occured at raw data line:" + (lineNo+1).ToString(), "Import Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                                return false;
                            }
                            string dte = (month + 1).ToString() + "/1/" + precipYear.ToString();
                            DateTime precipDate = Convert.ToDateTime(dte,CultureInfo.InvariantCulture);
                            command.CommandText = string.Format(insertStatement, xRef, yRef, dte, value);
                            command.ExecuteNonQuery();
                        }
                        precipYear++; // Increment our Year, This will be reset on next data block
                    }
                    else
                    {
                        // This is a header line - Record this if required
                        int findYear = lineData.IndexOf("[Years=");
                        if (findYear != -1)
                        {
                            startYear = Convert.ToInt16(lineData.Substring(findYear + 7, 4));
                        }
                        UpdateProgress("<Header> - " + lineData);
                    }
                    if (lineNo % 100 == 0)
                    {
                        pbProgress.Value = lineNo;
                    }
                }
                succeeded = true;
            }
            catch (IOException ioErr)
            {
                UpdateProgress(ioErr.Message.ToString());
                succeeded = false;
            }
            finally
            {
                if (sr != null) // Close stream reader if not null
                {
                    sr.Close();
                    sr = null;
                }
            }
            return succeeded;

        }

        private bool DatabaseExists(string databaseName)
        {
            // TODO - Parameterise the connection string

/*            jbaConnection = new SqlConnection("Server=" + localhost\\SQLEXPRESS;" +
                                    "Trusted_Connection=yes;" +
                                    "Database=" + databaseName + ";" +
                                    //"User Instance=true;" +
                                    "Connection timeout=5");
  */
            jbaConnection = new SqlConnection("Server="+SqlConnectionStringServer +
                                    "Trusted_Connection=yes;" +
                                    "Database=" + databaseName + ";" +
                                    //"User Instance=true;" +
                                    "Connection timeout=5");
            try
            {
                jbaConnection.Open();
                jbaConnection.Close();
                return true;
            }

            catch (SqlException ex)
            {
                return false;
            }
        }
        private bool DatabaseTableExists(string tableName)
        {
            // TODO - Parameterise the connection string
            jbaConnection = new SqlConnection("Server=" + SqlConnectionStringServer +
                                    "Trusted_Connection=yes;" +
                                    "Database=" + JbaDatabaseName + ";" +
                                    //"User Instance=true;" +
                                    "Connection timeout=5");
            try
            {
                SqlCommand command = new SqlCommand();
                command.Connection = jbaConnection;
                command.CommandType = CommandType.Text;
                command.Connection.Open();
                command.CommandText = "SELECT COUNT(*) FROM " + tableName;
                command.ExecuteNonQuery();
                jbaConnection.Close();
                return true;
            }

            catch (SqlException ex)
            {
                return false;
            }
        }

        private bool ExecuteSqlScript(string databaseName,string scriptName)
        {
            List<string> scriptText = GetFromResources(scriptName);
            if (scriptText.Count > 0)
            {
                
                string scriptCommand = "";
                for (int scriptLine = 0; scriptLine < scriptText.Count; scriptLine++)
                {
                    string cmd = scriptText[scriptLine];
                    if (cmd.Trim().ToUpper() == "GO")
                    {
                        SqlCommand command = new SqlCommand();

                        jbaConnection = new SqlConnection("Server=" + SqlConnectionStringServer +
                                                "Trusted_Connection=yes;" +
                                                "Database=" + databaseName + ";" +
                                                //"User Instance=true;" +
                                                "Connection timeout=5");

                        command.Connection = jbaConnection;
                        command.CommandType = CommandType.Text;
                        command.Connection.Open();
                        command.CommandText = scriptCommand;
                        command.ExecuteNonQuery();
                        jbaConnection.Close();
                        scriptCommand = "";
                    }
                    else
                    {
                        scriptCommand += cmd;
                        scriptCommand += "\r\n";
                    }
                }
            }
            return true;
        }

        // Read a resource text file
        // Results are contained in the returned List<>
        // Pass the name text resource.
        private List<string> GetFromResources(string resourceName)
        {
            string[] names = this.GetType().Assembly.GetManifestResourceNames();

            Assembly assem = Assembly.GetExecutingAssembly();
            string resource = "ImportPrecip.Resources." + resourceName; // assem.GetName().Name + '.' + resourceName;
            using (Stream stream = assem.GetManifestResourceStream(resource))
            {
                using (var reader = new StreamReader(stream))
                {
                    string text = reader.ReadToEnd();
                    List<string> list = new List<string>(
                           text.Split(new string[] { "\r\n" },
                           StringSplitOptions.RemoveEmptyEntries));
                    return list;
                }
            }
        }
        // Display progress
        private void UpdateProgress(string latestUpdate)
        {
            txtProgress.AppendText(latestUpdate + "\r\n");
            txtProgress.SelectionLength = 0;
            txtProgress.Refresh();
        }


 
        private void FrmMain_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.All;
            else
                e.Effect = DragDropEffects.None;

        }

        private void FrmMain_DragDrop(object sender, DragEventArgs e)
        {
            string[] fileList = (string[])e.Data.GetData(DataFormats.FileDrop, false);
            if (fileList.Length > 1)
            {
                MessageBox.Show("Please drag a single file only", "Invalid Drag Operation");
            }
            else
            {
                precipFile = fileList[0];
                UpdateProgress("> Import File:-");
                UpdateProgress(precipFile);
                btnConnect.Enabled = true;
            }
        }

        private void btnBrowse_Click(object sender, EventArgs e)
        {
            dlgOpen.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            dlgOpen.Filter = "Precipitation Files(*.pre)|*.pre|All Files (*.*)|*.*";
            dlgOpen.FilterIndex = 1;

            if (dlgOpen.ShowDialog() == DialogResult.OK)
            {
                //Get the path of specified file
                precipFile = dlgOpen.FileName;
                UpdateProgress("> Import File:-");
                UpdateProgress(precipFile);
                btnConnect.Enabled = true;
            }
        }

        private void btnExit_Click(object sender, EventArgs e)
        {
            try
            {
                if (jbaConnection.State == ConnectionState.Open)
                {
                    jbaConnection.Close();
                }
            }
            catch
            {
                // Nothiong to do
            }
            Close();
        }

        private void FrmMain_Load(object sender, EventArgs e)
        {
            TxtTestConnection.Text = SqlConnectionStringServer;
        }

        private void TxtTestConnection_Leave(object sender, EventArgs e)
        {
            SqlConnectionStringServer = TxtTestConnection.Text;
            if (SqlConnectionStringServer.IndexOf(";") == -1)
            {
                SqlConnectionStringServer += ";";
            }
            Properties.Settings.Default.SqlConnectionStringServer = SqlConnectionStringServer;
            Properties.Settings.Default.Save();
        }


        private void btnTestConnection_Click(object sender, EventArgs e)
        {
            try
            {
                UpdateProgress("> Testing SQL Connection. Please wait...");
                jbaConnection = new SqlConnection("Server=" + SqlConnectionStringServer +
                            "Trusted_Connection=yes;" +
                            "Database=master;" +
                            //"User Instance=true;" +
                            "Connection timeout=5");
                jbaConnection.Open();
                jbaConnection.Close();
                UpdateProgress("> Connection Succeeded");
                MessageBox.Show("Connection Succeeded", "Connection Test", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (SqlException ex)
            {
                UpdateProgress("> Connection Failed.");
                UpdateProgress("Error Returned from connection test:-");
                UpdateProgress(ex.ToString());
                MessageBox.Show("Connection Failed", "Connection Test", MessageBoxButtons.OK, MessageBoxIcon.Hand);
            }

        }

        private void BtnClear_Click(object sender, EventArgs e)
        {
            txtProgress.Text = "Progress:-\r\n";
        }
    }
}
