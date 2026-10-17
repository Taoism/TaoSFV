using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;

namespace TaoSFVLanguage.ViewModel
{
    public class MainWindowViewModel : INotifyPropertyChanged
    {
        private string _englishFilePath;
        private string _outputPath;
        private readonly DelegateCommand _generateCommand;

        public event PropertyChangedEventHandler PropertyChanged;

        public MainWindowViewModel()
        {
            // default to the TaoSFV project next to this tool's repository checkout
            string projectDir = FindProjectDirectory();
            _englishFilePath = projectDir == null ? "" : Path.Combine(projectDir, "languages", "english.txt");
            _outputPath = projectDir == null ? "" : Path.Combine(projectDir, "Localization");
            _generateCommand = new DelegateCommand(Generate);
        }

        private static string FindProjectDirectory()
        {
            for (DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, "TaoSFV");
                if (File.Exists(Path.Combine(candidate, "languages", "english.txt")))
                    return candidate;
            }

            return null;
        }

        public string EnglishFilePath
        {
            get { return _englishFilePath; }
            set
            {
                if (_englishFilePath != value)
                {
                    _englishFilePath = value;
                    SendPropertyChanged("EnglishFilePath");
                }
            }
        }

        public string OutputPath
        {
            get { return _outputPath; }
            set
            {
                if (_outputPath != value)
                {
                    _outputPath = value;
                    SendPropertyChanged("OutputPath");
                }
            }
        }

        public ICommand GenerateCommand
        {
            get { return _generateCommand; }
        }

        protected void SendPropertyChanged(string propertynName)
        {
            var handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(propertynName));
        }

        private void Generate()
        {
            string[] lines = File.ReadAllLines(EnglishFilePath);
            string className = null;
            List<string> classes = new List<string>();
            StringBuilder classText = null;
            bool isNewClass = true;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrEmpty(line))
                    continue;
                if (line.StartsWith(";"))
                    continue;

                if (line.StartsWith("###"))
                {
                    if (className != null)
                    {
                        WriteClass(className, classText);
                    }

                    className = line.Substring(4).Replace(" ", "");
                    classes.Add(className);

                    classText = new StringBuilder();
                    classText.AppendLine("namespace TaoSFV.Localization");
                    classText.AppendLine("{");
                    classText.AppendLine("    public class " + className);
                    classText.AppendLine("    {");

                    isNewClass = true;
                }
                else
                {
                    string propertyName = string.Empty;
                    int j;
                    for (j = 0; j < line.Length; j++)
                    {
                        if (line[j] == ' ')
                            break;
                        propertyName += line[j];
                    }
                    string value = line.Substring(j).TrimStart(new[] { ' ', '=' }).Trim();

                    if (!isNewClass)
                        classText.AppendLine();
                    else
                        isNewClass = false;

                    classText.AppendLine(string.Format("        ///<summary>{0}</summary>", value));
                    classText.AppendLine(string.Format("        public string {0} {{ get; set; }}", propertyName));
                }
            }

            WriteClass(className, classText);

            string path = Path.Combine(OutputPath, "Language.Generated.cs");
            StringBuilder langCS = new StringBuilder();

            langCS.AppendLine("namespace TaoSFV.Localization");
            langCS.AppendLine("{");
            langCS.AppendLine("    public static partial class Language");
            langCS.AppendLine("    {");
            foreach (string name in classes)
                langCS.AppendLine(string.Format("        public static {0} {0} {{ get; private set; }}", name));
            langCS.AppendLine("    }");
            langCS.AppendLine("}");

            File.WriteAllText(path, langCS.ToString());

            MessageBox.Show("Done.", "", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void WriteClass(string className, StringBuilder classText)
        {
            classText.AppendLine("    }");
            classText.AppendLine("}");

            if (!Directory.Exists(OutputPath))
                Directory.CreateDirectory(OutputPath);

            string path = Path.Combine(OutputPath, className + ".Generated.cs");
            File.WriteAllText(path, classText.ToString());
        }
    }
}
