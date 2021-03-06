﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Windows.Forms;
using Microsoft.Win32;
using XkeyBrew.Utils.DvdReader;

namespace xk3yDVDMenu
{

    public partial class Form1 : Form
    {

        private const int TitlesetISOLimit = 20;

        public string[][] AlphaGroups = new[]
                                            {
                                                new[] {"A", "B", "C"},
                                                new[] {"D", "E", "F"},
                                                new[] {"G", "H", "I"},
                                                new[] {"J", "K", "L"},
                                                new[] {"M", "N", "O"},
                                                new[] {"P", "Q", "R", "S"},
                                                new[] {"T", "U", "V"},
                                                new[] {"W", "X", "Y", "Z"}
                                            };

        public ArrayList GameISOs = new ArrayList();
        public Dictionary<string, object> Values = new Dictionary<string, object>();

        public string WorkingDirectory;
        public string TitleSets;
        public string PathToDVDStyler;
        public string PathToTheme;
        public string PathToVLC;

        ToolTip toolTip = new ToolTip();

        // 30 Games & Data loaded
        // 40 Page Templates created
        // 90 Transcoding complete
        // 100 Copied to drive
        public int PercentComplete = 0;

        public Form1()
        {
            InitializeComponent();
        }

        private void LoadDisks()
        {
            int selectedIndex = 0;
            int currentIndex = 0;

            DriveInfo[] driveList = DriveInfo.GetDrives();
            Log.Text += "Found " + driveList.Count() + (driveList.Count() == 1 ? " windows drive." : " windows drives.") + Environment.NewLine;

            comboBoxDriveList.Items.Clear();
            foreach (var drive in driveList)
            {
                if (drive.IsReady == true)
                {
                    string driveLabel;

                    if (drive.VolumeLabel.Length > 0)
                    {
                        driveLabel = drive.VolumeLabel;
                    }
                    else
                    {
                        switch (drive.DriveType)
                        {
                            case DriveType.CDRom:
                                driveLabel = "CD Drive";
                                break;
                            case DriveType.Fixed:
                                driveLabel = "Local Disk";
                                break;
                            case DriveType.Network:
                                driveLabel = "Network Drive";
                                break;
                            case DriveType.Removable:
                                driveLabel = "Removable Disk";
                                break;
                            default:
                                driveLabel = "Unknown";
                                break;
                        }
                    }

                    // Preselect drive
                    // We could filter on drives with `games` folder, but, I don't
                    // like the idea of scanning all the drives..
                    if (drive.DriveType == DriveType.Removable || drive.DriveType == DriveType.Network)
                    {
                        // Any removable drive is better than nothing.
                        if (selectedIndex == 0 && drive.DriveType == DriveType.Removable)
                        {
                            selectedIndex = currentIndex;
                        }
                        // HOWEVER, one with our name in it, that must be it!
                        bool containsXbox = driveLabel.IndexOf("Xbox", StringComparison.OrdinalIgnoreCase) >= 0;
                        bool containsxk3y = driveLabel.IndexOf("xk3y", StringComparison.OrdinalIgnoreCase) >= 0;
                        bool containsxkey = driveLabel.IndexOf("xkey", StringComparison.OrdinalIgnoreCase) >= 0;
                        if (containsXbox || containsxk3y || containsxkey)
                        {
                            selectedIndex = currentIndex;
                        }
                    }

                    string optionText = string.Format("({0}) {1} ({2})",
                        drive.Name.Substring(0, 2),
                        driveLabel,
                        GetBytesReadable(drive.TotalSize)
                        );
                    comboBoxDriveList.Items.Add(optionText);
                    currentIndex++;
                }
            }

            comboBoxDriveList.SelectedIndex = selectedIndex;

        }

        private string CreateDVDStylerTitleSets(IEnumerable<ISO> orderedISOs, BackgroundWorker worker)
        {

            Values.Add("TITLESETINDEX", "");
            Values.Add("TITLESETPGC", "");
            string strTitleset = "";

            var totalTitleSets = (int)Math.Ceiling((decimal)orderedISOs.Count() / TitlesetISOLimit);
            string titlesetHeader = (new StreamReader(string.Concat(PathToTheme, "titlesetHeader.xml"))).ReadToEnd();
            string titlesetSelected = (new StreamReader(string.Concat(PathToTheme, "titlesetSelected.xml"))).ReadToEnd();

            int currentTitleSet = 0;
            while (currentTitleSet < totalTitleSets)
            {
                Values["TITLESETPGC"] = string.Empty;

                IEnumerable<ISO> titlesetISO =
                    (from ISO d in orderedISOs orderby d.GameNameFromFilename select d).Skip(currentTitleSet * TitlesetISOLimit).Take(
                        TitlesetISOLimit).ToArray();

                string titlesetindex = "if (g0 == 1) jump menu 2;";
                for (int i = 2; i <= titlesetISO.Count(); i++)
                {
                    titlesetindex += "else if (g0 == " + i + ") jump menu " + (i + 1) + ";";
                }
                Values["TITLESETINDEX"] = titlesetindex;
                int isoindex = 0;

                foreach (ISO gameiso in titlesetISO)
                {
                    isoindex++;

                    gameiso.JumpToSelectThisGame = "g0 = " + isoindex + "; jump titleset " + (currentTitleSet + 1) + " menu;";

                    Values["JumpToSelectThisGame"] = gameiso.JumpToSelectThisGame;
                    Values["GAMETITLE"] = gameiso.GameTitle;
                    Values["GAMEGENRE"] = gameiso.GameGenre;
                    Values["GAMEDESC"] = gameiso.GameDesc;
                    Values["GAMEIMAGE"] = gameiso.Gameimage;
                    Values["GAMEBOX"] = gameiso.GameBoxart;

                    Values["TITLESETPGC"] += ThemeManager.ReplaceVals(titlesetSelected, Values);

                    ;

                }

                strTitleset += ThemeManager.ReplaceVals(titlesetHeader, Values);


                currentTitleSet++;
            }

            return strTitleset;
        }

        private void FindGameDetails(IEnumerable<ISO> orderedISOs, int buttonCount, BackgroundWorker worker)
        {
            int index = 0;
            int totalGames = orderedISOs.Count();

            foreach (ISO gameISO in orderedISOs)
            {
                index++;

                // 30 Games & Data loaded
                // 40 Page Templates created
                // 90 Transcoding complete
                // 100 Copied to drive
                PercentComplete = (int)Math.Round(((double)index / totalGames) * 30);

                string logOutput = string.Format("[{0} of {1}] ", index, totalGames) + 
                    gameISO.Filename + Environment.NewLine + "  ∟";
                
                gameISO.GameTitle = HttpUtility.HtmlEncode(gameISO.GetGameTitle(chkArtwork.Checked));
                gameISO.GameGenre = HttpUtility.HtmlEncode(gameISO.GetGameGenre(chkArtwork.Checked));
                gameISO.GameDesc = HttpUtility.HtmlEncode(gameISO.GetGameDesc(chkArtwork.Checked));
                gameISO.Gameimage = HttpUtility.HtmlEncode(gameISO.GetGameBanner(chkArtwork.Checked));
                gameISO.GameBoxart = HttpUtility.HtmlEncode(gameISO.GetGameBoxart(chkArtwork.Checked));

                logOutput += gameISO.GameTitle.Length > 0 ? "[Title]" : "       ";
                logOutput += gameISO.GameGenre.Length > 0 ? "[Genre]" : "       ";
                logOutput += gameISO.GameDesc.Length > 0 ? "[Desc]" : "      ";
                logOutput += gameISO.Gameimage.Length > 0 ? "[Banner]" : "        ";
                logOutput += gameISO.GameBoxart.Length > 0 ? "[Cover]" : "       ";
                
                logOutput += Environment.NewLine;

                worker.ReportProgress(PercentComplete, logOutput);

                gameISO.Page = (int) Math.Floor((double) index) / buttonCount;
            }
        }

        private IEnumerable<ISO> LoadGameDetails(IEnumerable<ISO> orderedISOs, int buttonCount, BackgroundWorker worker)
        {
            var serializer = new BinaryFormatter();

            // Check previous search results
            if (chkUseCache.Checked == true &&
                File.Exists(WorkingDirectory + "game-data-" + Values["DRIVE_LETTER"] + ".dat") &&
                File.Exists(WorkingDirectory + "cached-results-" + Values["DRIVE_LETTER"] + ".dat") &&
                FilesAreIdentical(WorkingDirectory + "current-results-" + Values["DRIVE_LETTER"] + ".dat", WorkingDirectory + "cached-results-" + Values["DRIVE_LETTER"] + ".dat"))
            {
                // Deserialize cached IEnumerable<ISO> orderedISOs
                using (var stream = File.OpenRead(WorkingDirectory + "game-data-" + Values["DRIVE_LETTER"] + ".dat"))
                {
                    orderedISOs = (ISO[])serializer.Deserialize(stream);
                }

                worker.ReportProgress(PercentComplete, "Using Cached Game Data..." + Environment.NewLine + Environment.NewLine);

                int index = 0;
                int totalGames = orderedISOs.Count();

                foreach (ISO gameISO in orderedISOs)
                {
                    index++;

                    // 30 Games & Data loaded
                    // 40 Page Templates created
                    // 90 Transcoding complete
                    // 100 Copied to drive
                    PercentComplete = (int)Math.Round(((double)index / totalGames) * 30);

                    string logOutput = string.Format("[{0} of {1}] ", index, totalGames) +
                        gameISO.Filename + Environment.NewLine + "  ∟";

                    logOutput += gameISO.GameTitle.Length > 0 ? "[Title]" : "       ";
                    logOutput += gameISO.GameGenre.Length > 0 ? "[Genre]" : "       ";
                    logOutput += gameISO.GameDesc.Length > 0 ? "[Desc]" : "      ";
                    logOutput += gameISO.Gameimage.Length > 0 ? "[Banner]" : "        ";
                    logOutput += gameISO.GameBoxart.Length > 0 ? "[Cover]" : "       ";

                    logOutput += Environment.NewLine;

                    worker.ReportProgress(PercentComplete, logOutput);
                }

                // Disregard current (identical) results
                if (File.Exists(WorkingDirectory + "current-results-" + Values["DRIVE_LETTER"] + ".dat"))
                {
                    File.Delete(WorkingDirectory + "current-results-" + Values["DRIVE_LETTER"] + ".dat");
                }
            }
            else
            {
                worker.ReportProgress(PercentComplete, Environment.NewLine);

                // Finds Game resources inc Title & populates orderedISOs entries
                FindGameDetails(orderedISOs, buttonCount, worker);

                // Serialize orderedISOs to file
                using (var stream = File.Create(WorkingDirectory + "game-data-" + Values["DRIVE_LETTER"] + ".dat"))
                {
                    serializer.Serialize(stream, orderedISOs);
                }

                // Replace cached-results with the current-results
                if (File.Exists(WorkingDirectory + "cached-results-" + Values["DRIVE_LETTER"] + ".dat"))
                {
                    File.Delete(WorkingDirectory + "cached-results-" + Values["DRIVE_LETTER"] + ".dat");
                }
                File.Move(WorkingDirectory + "current-results-" + Values["DRIVE_LETTER"] + ".dat", WorkingDirectory + "cached-results-" + Values["DRIVE_LETTER"] + ".dat");
            }

            return orderedISOs;
        }

        private void backgroundWorker1_DoWork(object sender, DoWorkEventArgs e)
        {
            // Get the BackgroundWorker that raised this event.
            BackgroundWorker worker = sender as BackgroundWorker;

            FetchGameDataAndCreateProject(worker, e);
        }

        private void backgroundWorker1_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            progressBar1.Value = e.ProgressPercentage;
            Log.Text += e.UserState as String;
        }

        private void backgroundWorker1_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (GameISOs.Count > 0)
            {
                buttonBuildProject.Enabled = false;
                buttonTranscodeMenu.Enabled = true;
                buttonCopyToDrive.Enabled = false;
                
                buttonTranscodeMenu.Focus();

                Log.Text += Environment.NewLine;
                Log.Text += "Step 1 of 3 Complete." + Environment.NewLine + Environment.NewLine;

                // 30 Games & Data loaded
                // 40 Page Templates created
                // 90 Transcoding complete
                // 100 Copied to drive
                progressBar1.Value = 40;
            }
            else
            {
                comboBoxDriveList.Enabled = true;
                comboBoxThemeList.Enabled = true;

                chkArtwork.Enabled = true;
                chkUseCache.Enabled = true;
                buttonBuildProject.Enabled = true;

                MessageBox.Show("No Games found.");
                comboBoxDriveList.Enabled = true;
                comboBoxThemeList.Enabled = true;

                progressBar1.Value = 0;
            }
        }

        private void FetchGameDataAndCreateProject(BackgroundWorker worker, DoWorkEventArgs e)
        {

            // Copy media files to WorkingDirectory
            CopyFolder(Application.StartupPath + "\\media", WorkingDirectory + "media");

            // Copy theme to WorkingDirectory
            CopyFolder(Application.StartupPath + "\\Themes\\" + Values["THEME"], PathToTheme);

            // Update theme files in WorkingDirectory from .txt to .xml as needed
            string filename;
            string[] filePaths = Directory.GetFiles(PathToTheme, "*.txt");
            foreach (string myfile in filePaths)
            {
                filename = Path.ChangeExtension(myfile, ".xml");
                try
                {
                    File.Delete(filename);
                    File.Move(myfile, filename);
                }
                catch (Exception)
                {
                    throw;
                }
            }

            // Create cache folder
            if (!Directory.Exists(WorkingDirectory + "cache"))
            {
                Directory.CreateDirectory(WorkingDirectory + "cache");
            }

            GameISOs.Clear();
            if (Directory.Exists(string.Concat(Values["DRIVE"], "games\\")))
            {
                // Populates GameISOs
                RecursiveISOSearch(string.Concat(Values["DRIVE"], "games\\"));
            }

            // Limit for testing..
            int limitForTesting = 20;
            if (GameISOs.Count > limitForTesting)
            {
                GameISOs = GameISOs.GetRange(0, limitForTesting);
            }
            
            worker.ReportProgress(PercentComplete, "Found " + GameISOs.Count + (GameISOs.Count == 1 ? " ISO." : " ISOs.") + Environment.NewLine);

            // Serialize search results to file
            var serializer = new BinaryFormatter();
            using (var stream = File.Create(WorkingDirectory + "current-results-" + Values["DRIVE_LETTER"] + ".dat"))
            {
                serializer.Serialize(stream, GameISOs);
            }
            
            if (GameISOs.Count > 0)
            {
                worker.ReportProgress(PercentComplete, Environment.NewLine + 
                    "Retrieving Game Info & Media..." + Environment.NewLine);

                Values.Add("APPPATH", WorkingDirectory);
                Values.Add("PAGEINDEX", 0);
                Values.Add("PAGE", 1);
                Values.Add("ISOID", 0);
                Values.Add("CONTENT", "");
                Values.Add("GAMEIMAGE", "");
                Values.Add("GAMETITLE", "");
                Values.Add("GAMEGENRE", "");
                Values.Add("GAMEDESC", "");
                Values.Add("GAMEBOX", "");

                Values.Add("JumpToSelectThisGame", "");

                string pgc = (new StreamReader(PathToTheme + "PGC.xml")).ReadToEnd();
                int buttonCount =
                    (from d in new DirectoryInfo(PathToTheme).GetFiles("ButtonLocation*.xml") select d).Count();
                double totalPages = Math.Ceiling(GameISOs.Count/(double) buttonCount);
                Values.Add("TotalPageCount", totalPages);
                string buttonDef = (new StreamReader(PathToTheme + "ButtonStyle.xml")).ReadToEnd();
                string objDef = (new StreamReader(PathToTheme + "GAMEOBJ.xml")).ReadToEnd();
                string butActions = (new StreamReader(PathToTheme + "ButtonActions.xml")).ReadToEnd();
                string objFiles = (new StreamReader(PathToTheme + "OBJFiles.xml")).ReadToEnd();
                string prevDef = (new StreamReader(PathToTheme + "PrevButtonStyle.xml")).ReadToEnd();
                string prevLoc = (new StreamReader(PathToTheme + "PrevButtonLocation.xml")).ReadToEnd();
                string prevAct = (new StreamReader(PathToTheme + "PrevButtonAction.xml")).ReadToEnd();
                string nextDef = (new StreamReader(PathToTheme + "NextButtonStyle.xml")).ReadToEnd();
                string nextLoc = (new StreamReader(PathToTheme + "NextButtonLocation.xml")).ReadToEnd();
                string nextAct = (new StreamReader(PathToTheme + "NextButtonAction.xml")).ReadToEnd();
                string alphaDef = (new StreamReader(PathToTheme + "alphaButtonStyle.xml")).ReadToEnd();
                string alphaLoc = (new StreamReader(PathToTheme + "alphaButtonLocation.xml")).ReadToEnd();
                string alphaAct = (new StreamReader(PathToTheme + "alphaButtonAction.xml")).ReadToEnd();

                string pgcs = "";

                ISO[] orderedISOs = (from ISO d in GameISOs orderby d.GameNameFromFilename select d).ToArray();
                orderedISOs = (ISO[])LoadGameDetails(orderedISOs, buttonCount, worker);

                worker.ReportProgress(PercentComplete, Environment.NewLine + "Creating Templates (this can take a while)..." + Environment.NewLine);

                TitleSets = CreateDVDStylerTitleSets(orderedISOs, worker);

                for (int currentPage = 0; (double)currentPage < totalPages; currentPage++)
                {
                    string defs = "<defs id=\"defs\">\n";
                    string locationsObJ = "<g id=\"objects\">\n";
                    string locationsBut = "<g id=\"buttons\">\n";
                    string actions = "";
                    string defObjs = "";
                    string objFilestxt = "";
                    Values["PAGE"] = currentPage + 1;
                    Values["PAGEINDEX"] = 0;

                    var pageISOs = (from ISO d in orderedISOs orderby d.GameNameFromFilename select d).Skip(currentPage*buttonCount).Take(buttonCount);

                    Values["PageButtonCount"] = pageISOs.Count();

                    foreach (ISO currentISO in pageISOs)
                    {
                        Application.DoEvents();

                        Values["PAGEINDEX"] = string.Format("{0:00}", int.Parse(Values["PAGEINDEX"].ToString()) + 1);
                        Values["ISOID"] = (int) Values["ISOID"] + 1;
                        Values["GAMETITLE"] = currentISO.GameTitle;
                        Values["GAMEGENRE"] = currentISO.GameGenre;
                        Values["GAMEDESC"] = currentISO.GameDesc;
                        Values["GAMEIMAGE"] = currentISO.Gameimage;
                        Values["GAMEBOX"] = currentISO.GameBoxart;
                        Values["JumpToSelectThisGame"] = currentISO.JumpToSelectThisGame;

                        string pathToObjectLocationFile = PathToTheme + "ObjLocation" + Values["PAGEINDEX"] + ".xml";
                        string objectLocation = (new StreamReader(pathToObjectLocationFile)).ReadToEnd();
                        
                        string pathToButtonLocationsFile = PathToTheme + "ButtonLocation" + Values["PAGEINDEX"] + ".xml";
                        string buttonLocations = (new StreamReader(pathToButtonLocationsFile)).ReadToEnd();

                        defs += ThemeManager.ReplaceVals(buttonDef, Values);
                        defObjs += ThemeManager.ReplaceVals(objDef, Values);
                        locationsObJ += ThemeManager.ReplaceVals(objectLocation, Values);
                        locationsBut += ThemeManager.ReplaceVals(buttonLocations, Values);
                        actions += ThemeManager.ReplaceVals(butActions, Values);
                        objFilestxt += ThemeManager.ReplaceVals(objFiles, Values);
                    }

                    if (File.Exists(PathToTheme + "alpha.xml"))
                    {
                        defs += ThemeManager.ReplaceVals(alphaDef, Values);
                        locationsBut += ThemeManager.ReplaceVals(alphaLoc, Values);
                        actions += ThemeManager.ReplaceVals(alphaAct, Values);
                    }

                    if (currentPage > 0)
                    {
                        defs += ThemeManager.ReplaceVals(prevDef, Values);
                        locationsBut += ThemeManager.ReplaceVals(prevLoc, Values);
                        actions += ThemeManager.ReplaceVals(prevAct, Values);
                    }

                    if (totalPages > (currentPage + 1))
                    {
                        defs += ThemeManager.ReplaceVals(nextDef, Values);
                        locationsBut += ThemeManager.ReplaceVals(nextLoc, Values);
                        actions += ThemeManager.ReplaceVals(nextAct, Values);
                    }

                    locationsObJ += "</g>\n";
                    locationsBut += "</g>\n";
                    defs += defObjs + "</defs>\n";

                    Values["CONTENT"] = defs + locationsObJ + locationsBut + "</svg>\n" + actions + objFilestxt;

                    pgcs += ThemeManager.ReplaceVals(pgc, Values);
                }

                if (File.Exists(PathToTheme + "alpha.xml"))
                {
                    string allactions = "";
                    Values.Add("alphaletter", "A");
                    Values.Add("alphaaction", "");
                    Values.Add("alphaActions", "");

                    string alpha = (new StreamReader(PathToTheme + "alpha.xml")).ReadToEnd();
                    string alphaActions = (new StreamReader(PathToTheme + "alpha-Actions.xml")).ReadToEnd();
                    foreach (var letterGroup in AlphaGroups)
                    {
                        int PreviousFound = 0;
                        foreach (string letter in letterGroup)
                        {
                            int found = FirstLocationAlpha(letter, buttonCount);
                            if (found > -1)
                            {
                                PreviousFound = found;
                                break;
                            }
                        }

                        Values["alphaaction"] = string.Concat("jump vmgm menu ", PreviousFound + 1, ";");
                        Values["alphaletter"] = letterGroup[0];

                        allactions += ThemeManager.ReplaceVals(alphaActions, Values);
                    }
                    Values["alphaActions"] = allactions;
                    pgcs += ThemeManager.ReplaceVals(alpha, Values);
                }

                Values.Add("PGCS", pgcs);
                Values.Add("TITLESETS", TitleSets);
                string mainfile = (new StreamReader(PathToTheme + "Main.xml")).ReadToEnd();
                mainfile = ThemeManager.ReplaceVals(mainfile, Values);

                // 30 Games & Data loaded
                // 40 Page Templates created
                // 90 Transcoding complete
                // 100 Copied to drive
                PercentComplete = 40;
                worker.ReportProgress(PercentComplete, 
                    "Saving Project File..." + Environment.NewLine +
                    "file://" + WorkingDirectory + "project.dvds" + Environment.NewLine);

                // Write our project file (XML)
                var projectFile = new StreamWriter(WorkingDirectory + "project.dvds", false);
                var chrArray = new char[1];
                chrArray[0] = '\n';
                string[] strArrays1 = mainfile.Split(chrArray);
                int num1 = 0;
                while (num1 < strArrays1.Length)
                {
                    string line = strArrays1[num1];
                    if (!line.Trim().StartsWith("//"))
                    {
                        projectFile.Write(line);
                    }
                    num1++;
                }
                projectFile.Close();
            }
        }

        private int FirstLocationAlpha(string letter, int buttonCount)
        {
            ISO[] orderedISO = (from ISO gameISO in GameISOs orderby gameISO.GameNameFromFilename select gameISO).ToArray();

            int num = 0;
            while (num < orderedISO.Length)
            {
                ISO gameISO = orderedISO[num];

                if (!gameISO.GameNameFromFilename.ToUpper().StartsWith(letter))
                {
                    num++;
                }
                else
                {
                    return gameISO.Page;
                }
            }

            return -1;
        }

        private void CreateSectorMap()
        {
            Log.Text += "Creating Sector Map..." + Environment.NewLine;

            var sectorMapFile = new StreamWriter(WorkingDirectory + "dvd.xsk", false);
            try
            {
                var sha = new SHA1CryptoServiceProvider();
                var encoding = new UTF8Encoding();

                // https://code.google.com/p/xkey-brew/source/browse/Utils/DvdReader/DvdMenuReadSectors.cs
                // We can match Sectors and ISOs by the order they are in, write out sector map with xk3y Game IDs

                var sectors = new DvdMenuReadSectors(WorkingDirectory + "dvd.iso").FillListWithMenuSectors();
                var orderedISOs = (from ISO gameISO in GameISOs orderby gameISO.GameNameFromFilename select gameISO).ToArray();

                int i = 0;
                foreach (var sector in sectors)
                {
                    // E:\games\FIFA 14\FIFA 14 [15467F11].iso
                    string gamePath = orderedISOs[i].Path;

                    // games/FIFA 14/FIFA 14 [15467F11].iso
                    gamePath = gamePath.Replace(Values["DRIVE"].ToString(), "").Replace("\\", "/");

                    // FIFA 14/FIFA 14 [15467F11].iso
                    gamePath = gamePath.Substring("games/".Length);

                    // "FIFA 14/FIFA 14 [15467F11].iso" as sequence of bytes
                    byte[] data = encoding.GetBytes(gamePath);

                    // Game ID, e.g. 39677831299d46ac508bf532afba24cb1c05248c
                    byte[] hash = sha.ComputeHash(data);

                    sectorMapFile.BaseStream.Write(sector, 0, 4);
                    sectorMapFile.BaseStream.Write(hash, 0, hash.Length);

                    i++;
                }
                sectorMapFile.BaseStream.Flush();
                sectorMapFile.Flush();
                sectorMapFile.Close();
            }
            catch //(Exception ex)
            {
                throw;
            }
        }

        private void LoadThemes()
        {
            var themePaths = new DirectoryInfo(Application.StartupPath + "\\themes\\");
            var themeList = themePaths.GetDirectories();

            Log.Text += "Found " + themeList.Count() + (themeList.Count() == 1 ? " theme." : " themes.") + Environment.NewLine;

            comboBoxThemeList.Items.Clear();
            foreach (DirectoryInfo theme in themeList)
            {
                comboBoxThemeList.Items.Add(theme.Name);
            }
            comboBoxThemeList.SelectedIndex = 0;
        }

        private bool isDVDStylerInstalled()
        {
            PathToDVDStyler = ProgramFilesx86() + "\\DVDStyler\\bin\\DVDStyler.exe";

            if (File.Exists(PathToDVDStyler))
            {
                // DVDStyler doesn't store version info in its executable...
                string version = (string)Registry.GetValue(@"HKEY_CURRENT_USER\SOFTWARE\DVDStyler", "Version", "is installed");

                Log.Text += "DVDStyler " + version + Environment.NewLine;

                return true;
            }
            else
            {
                Log.Text += "DVDStyler not found." + Environment.NewLine;
                Log.Text += "Get it at http://www.dvdstyler.org" + Environment.NewLine;

                return false;
            }

        }

        private bool isVLCInstalled()
        {
            PathToVLC = ProgramFilesx86() + "\\VideoLAN\\VLC\\vlc.exe";
            return File.Exists(PathToVLC);
        }
        
        private void SetupWorkingDirectory()
        {
            // Current User Roaming App Data - equivalent to %AppData% if set
            string pathToRoamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            // Combine the App Data\Roaming path with our desired directory name
            WorkingDirectory = Path.Combine(pathToRoamingAppData, "xk3y-DVDMenu\\");

            if (!Directory.Exists(WorkingDirectory))
            {
                Directory.CreateDirectory(WorkingDirectory);
            }
        }

        // Returns the human-readable file size for an arbitrary, 64-bit file size 
        // The default format is "0.## XB", e.g. "4.2 KB" or "1.43 GB"
        public static string GetBytesReadable(long i)
        {
            string sign = (i < 0 ? "-" : "");
            double readable = (i < 0 ? -i : i);
            string suffix;
            if (i >= 0x1000000000000000) // Exabyte
            {
                suffix = "EB";
                readable = (double)(i >> 50);
            }
            else if (i >= 0x4000000000000) // Petabyte
            {
                suffix = "PB";
                readable = (double)(i >> 40);
            }
            else if (i >= 0x10000000000) // Terabyte
            {
                suffix = "TB";
                readable = (double)(i >> 30);
            }
            else if (i >= 0x40000000) // Gigabyte
            {
                suffix = "GB";
                readable = (double)(i >> 20);
            }
            else if (i >= 0x100000) // Megabyte
            {
                suffix = "MB";
                readable = (double)(i >> 10);
            }
            else if (i >= 0x400) // Kilobyte
            {
                suffix = "KB";
                readable = (double)i;
            }
            else
            {
                return i.ToString(sign + "0 B"); // Byte
            }
            readable /= 1024;

            return sign + readable.ToString("0.## ") + suffix;
        }

        static string ProgramFilesx86()
        {
            if (8 == IntPtr.Size
                || (!String.IsNullOrEmpty(Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432"))))
            {
                return Environment.GetEnvironmentVariable("ProgramFiles(x86)");
            }

            return Environment.GetEnvironmentVariable("ProgramFiles");
        }

        private void Form1Load(object sender, EventArgs e)
        {
            Log.Text += "xk3y DVDMenu Tool" + Environment.NewLine;

            buttonBuildProject.Enabled = isDVDStylerInstalled();
            chkPreview.Checked = isVLCInstalled();

            SetupWorkingDirectory();

            Log.Text += Environment.NewLine;

            // Populate comboBoxes
            LoadDisks();
            LoadThemes();

            this.ActiveControl = buttonBuildProject;
        }

        private void RecursiveISOSearch(string targetDirectory)
        {
            foreach (FileInfo fileInfo in new DirectoryInfo(targetDirectory).GetFiles("*.ISO"))
            {
                GameISOs.Add(new ISO
                                   {
                                       Filename = fileInfo.Name,
                                       GameNameFromFilename = Regex.Replace(fileInfo.Name, ".iso", "", RegexOptions.IgnoreCase),
                                       Path = fileInfo.FullName,
                                       ISOFile = fileInfo,
                                       WorkingDirectory = WorkingDirectory
                                   });
            }
            try
            {
                foreach (string directory in Directory.GetDirectories(targetDirectory))
                {
                    RecursiveISOSearch(directory);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        private void buttonBuildProject_Click(object sender, EventArgs e)
        {
            comboBoxDriveList.Enabled = false;
            comboBoxThemeList.Enabled = false;

            chkArtwork.Enabled = false;
            chkUseCache.Enabled = false;
            buttonBuildProject.Enabled = false;

            Values.Clear();

            Values.Add("THEME", comboBoxThemeList.SelectedItem);
            PathToTheme = WorkingDirectory + "\\Themes\\" + Values["THEME"] + "\\";

            // Selected item string expected as "(Z:)..."
            Values.Add("DRIVE", comboBoxDriveList.SelectedItem.ToString().Substring(1, 2) + "\\");
            Values.Add("DRIVE_LETTER", Values["DRIVE"].ToString().Substring(0, 1));

            // FetchGameDataAndCreateProject() in new thread
            backgroundWorker1.RunWorkerAsync();
        }

        private void buttonTranscodeMenu_Click(object sender, EventArgs e)
        {

            if (File.Exists(WorkingDirectory + "dvd.iso"))
            {
                try
                {
                    File.Delete(WorkingDirectory + "dvd.iso");
                }
                catch (Exception)
                {
                    MessageBox.Show("Unable to delete existing ISO file");
                    return;
                }
            }

            chkPreview.Enabled = false;

            Log.Text += "Launching DVDStyler..." + Environment.NewLine;

            var start = new ProcessStartInfo
                            {
                                FileName = PathToDVDStyler,
                                Arguments = " --stderr --start \"" + WorkingDirectory + "project.dvds\"",
                            };

            using (Process DVDStylerProcess = Process.Start(start))
            {
                DVDStylerProcess.WaitForExit();
                foreach (FileInfo fileInfo in new DirectoryInfo(WorkingDirectory).GetFiles("*.vob"))
                {
                    fileInfo.Delete();
                }

                if (File.Exists(WorkingDirectory + "dvd.iso"))
                {
                    CreateSectorMap();

                    buttonBuildProject.Enabled = false;
                    buttonTranscodeMenu.Enabled = false;
                    buttonCopyToDrive.Enabled = true;

                    buttonCopyToDrive.Focus();

                    Log.Text += Environment.NewLine;
                    Log.Text += "Step 2 of 3 Complete." + Environment.NewLine + Environment.NewLine;

                    // 30 Games & Data loaded
                    // 40 Page Templates created
                    // 90 Transcoding complete
                    // 100 Copied to drive
                    progressBar1.Value = 90;

                    // Preview before copying to drive
                    if (chkPreview.Checked == true && isVLCInstalled())
                    {
                        var previewInVLC = new ProcessStartInfo
                        {
                            FileName = PathToVLC,
                            Arguments = " \"" + WorkingDirectory + "dvd.iso\"",
                        };
                        Process.Start(previewInVLC);
                    }

                }
                else
                {
                    MessageBox.Show("ISO creation failed!");
                    chkPreview.Enabled = true;
                }
            }
        }
        
        private void buttonCopyToDrive_Click(object sender, EventArgs e)
        {
            try
            {
                File.Copy(WorkingDirectory + "dvd.iso", Values["DRIVE"] + "games\\menu.xso", true);
                File.Copy(WorkingDirectory + "dvd.xsk", Values["DRIVE"] + "games\\menu.xsk", true);
                Log.Text += "Step 3 of 3 Complete." + Environment.NewLine;

                // 30 Games & Data loaded
                // 40 Page Templates created
                // 90 Transcoding complete
                // 100 Copied to drive
                progressBar1.Value = 100;

                buttonCopyToDrive.Enabled = false;

                Log.Text += Environment.NewLine;
                Log.Text += "Make sure your xkey.cfg has MENUDVD=Y and enjoy :)" + Environment.NewLine;

                // TODO: timer for ~5 seconds then unlock (reset) all controls..

            }
            catch (Exception)
            {
                MessageBox.Show("Could not copy to drive.");
            }
        }
        
        private void Log_TextChanged(object sender, EventArgs e)
        {
            Log.SelectionStart = Log.Text.Length;
            Log.ScrollToCaret();
        }

        private void Log_KeyDown(object sender, KeyEventArgs e)
        {
            // Ctrl+A support
            if (e.Control & e.KeyCode == Keys.A)
            {
                Log.SelectAll();
            }
        }

        private void pictureBoxLogo_Click(object sender, EventArgs e)
        {
            Process.Start("http://k3yforums.com/");
        }

        private void pictureBoxLogo_MouseHover(object sender, EventArgs e)
        {
            toolTip.Show("Visit k3y Forums", pictureBoxLogo, 32000);
        }

        private void comboBoxDriveList_DropDown(object sender, EventArgs e)
        {
            // Extend width of list beyond the ComboBox control as needed 

            ComboBox senderComboBox = (ComboBox)sender;
            int width = senderComboBox.DropDownWidth;
            Graphics g = senderComboBox.CreateGraphics();
            Font font = senderComboBox.Font;
            int vertScrollBarWidth =
                (senderComboBox.Items.Count > senderComboBox.MaxDropDownItems)
                ? SystemInformation.VerticalScrollBarWidth : 0;
            int newWidth;

            foreach (string s in ((ComboBox)sender).Items)
            {
                newWidth = (int)g.MeasureString(s, font).Width
                    + vertScrollBarWidth;

                if (width < newWidth)
                {
                    width = newWidth;
                }
            }

            senderComboBox.DropDownWidth = width;
        }

        private void comboBoxThemeList_DropDown(object sender, EventArgs e)
        {
            // Extend width of list beyond the ComboBox control as needed 

            ComboBox senderComboBox = (ComboBox)sender;
            int width = senderComboBox.DropDownWidth;
            Graphics g = senderComboBox.CreateGraphics();
            Font font = senderComboBox.Font;
            int vertScrollBarWidth =
                (senderComboBox.Items.Count > senderComboBox.MaxDropDownItems)
                ? SystemInformation.VerticalScrollBarWidth : 0;
            int newWidth;

            foreach (string s in ((ComboBox)sender).Items)
            {
                newWidth = (int)g.MeasureString(s, font).Width
                    + vertScrollBarWidth;

                if (width < newWidth)
                {
                    width = newWidth;
                }
            }

            senderComboBox.DropDownWidth = width;
        }


        // Copy folder to another folder recursively
        public static void CopyFolder(string sourceFolder, string destFolder)
        {
            if (!Directory.Exists(destFolder))
                Directory.CreateDirectory(destFolder);
            string[] files = Directory.GetFiles(sourceFolder);
            foreach (string file in files)
            {
                string name = Path.GetFileName(file);
                string dest = Path.Combine(destFolder, name);
                File.Copy(file, dest, true);
            }
            string[] folders = Directory.GetDirectories(sourceFolder);
            foreach (string folder in folders)
            {
                string name = Path.GetFileName(folder);
                string dest = Path.Combine(destFolder, name);
                CopyFolder(folder, dest);
            }
        }


        // Compares files from provided path strings
        // A return value of true indicates a match
        private bool FilesAreIdentical(string file1, string file2)
        {
            int file1byte;
            int file2byte;
            FileStream fs1;
            FileStream fs2;

            // Determine if the same file was referenced two times.
            if (file1 == file2)
            {
                // Return true to indicate that the files are the same.
                return true;
            }

            // Open the two files.
            fs1 = new FileStream(file1, FileMode.Open, FileAccess.Read);
            fs2 = new FileStream(file2, FileMode.Open, FileAccess.Read);

            // Check the file sizes. If they are not the same, the files 
            // are not the same.
            if (fs1.Length != fs2.Length)
            {
                // Close the file
                fs1.Close();
                fs2.Close();

                // Return false to indicate files are different
                return false;
            }

            // Read and compare a byte from each file until either a
            // non-matching set of bytes is found or until the end of
            // file1 is reached.
            do
            {
                // Read one byte from each file.
                file1byte = fs1.ReadByte();
                file2byte = fs2.ReadByte();
            }
            while ((file1byte == file2byte) && (file1byte != -1));

            // Close the files.
            fs1.Close();
            fs2.Close();

            // Return the success of the comparison. "file1byte" is 
            // equal to "file2byte" at this point only if the files are 
            // the same.
            return ((file1byte - file2byte) == 0);
        }

        private void chkArtwork_MouseHover(object sender, EventArgs e)
        {
            toolTip.Show("Download missing Artwork from Xbox.com", chkArtwork, 32000);
        }

        private void chkPreview_MouseHover(object sender, EventArgs e)
        {
            toolTip.Show("Preview using VLC", chkPreview, 32000);
        }

        private void chkUseCache_MouseHover(object sender, EventArgs e)
        {
            toolTip.Show("Uncheck if ISOs haven't changed but\nMedia / XML / Download Artwork choice has", chkUseCache, 32000);
        }

        private void Log_LinkClicked(object sender, LinkClickedEventArgs e)
        {
            Process.Start(e.LinkText);
        }

    }
}