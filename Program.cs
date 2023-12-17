using System;
using System.IO;
using System.Text;
using System.Media;
using System.Management;
using System.Threading.Tasks;
using IniParser;
using IniParser.Model;

namespace G435BatteryApproximate
{
    internal class Program
    {
        private const int UPDATE_DELAY_MILLISECONDS = 5000;
        private const string CACHE_FILENAME = "cache";
        private const string SETTING_FILENAME = "setting.ini";

        public static readonly int DefaultBatteryUsageHours = 18;
        public static readonly int DefaultBatteryUsageHoursCorrectionPercentage = 95;
        public static readonly int DefaultBatteryHealthPercentage = 100;

        public static async Task Main(string[] args)
        {
            var setting = LoadSetting();

            Console.WriteLine("Approximate G435 battery life, assume battery full charge");
            Console.WriteLine("Choose options : ");
            Console.WriteLine("1) Calculate from system boot time");
            Console.WriteLine("2) Calculate from last saved time");
            Console.WriteLine("3) Calculate from current time (overwrite last saved time)");
            Console.Write("Option : ");

            var keyInfo = Console.ReadKey();
            var strOption = keyInfo.Key switch
            {
                ConsoleKey.D1 => "1",
                ConsoleKey.D2 => "2",
                ConsoleKey.D3 => "3",
                _ => "-1"
            };

            Console.WriteLine();

            int optionNumber;
            bool canParseOptionNumber = int.TryParse(strOption, out optionNumber);
            bool isOptionSupport = canParseOptionNumber && (optionNumber > 0) && (optionNumber <= 3);

            if (isOptionSupport)
            {
                DateTime startTime;

                try
                {
                    startTime = optionNumber switch
                    {
                        1 => GetSystemBootTime(),
                        2 => GetLastSavedDateTime(),
                        3 => GetCurrentTime(),
                        _ => DateTime.Now,
                    };
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine(e);
                    Console.ReadLine();
                    return;
                }

                bool shouldOverWriteLastDateTime = (optionNumber == 3);

                if (shouldOverWriteLastDateTime)
                {
                    await SaveLastDateTime(startTime, CACHE_FILENAME);
                }

                int batteryUsageHour = int.Parse(setting["Battery"]["batteryUsageHour"]);
                int batteryUsageCorrectionPercentage = int.Parse(setting["Battery"]["batteryUsageCorrectionPercentage"]);
                int batteryHealthPercentage = int.Parse(setting["Battery"]["batteryHealthPercentage"]);

                batteryUsageHour = Math.Abs(batteryUsageHour);
                batteryUsageCorrectionPercentage = Math.Abs(batteryUsageCorrectionPercentage) > 100 ? 100 : Math.Abs(batteryUsageCorrectionPercentage);
                batteryHealthPercentage = Math.Abs(batteryHealthPercentage) > 100 ? 100 : Math.Abs(batteryHealthPercentage);

                var emptyBatteryDate = GetApproximateEmptyBatteryDateTime(
                    startTime,
                    batteryUsageHour,
                    batteryUsageCorrectionPercentage,
                    batteryHealthPercentage
                );

                SystemSounds.Beep.Play();

                while (true)
                {
                    var timeLeft = (emptyBatteryDate - DateTime.Now);

                    if (timeLeft < TimeSpan.Zero)
                    {
                        timeLeft = TimeSpan.Zero;
                    }

                    Console.Clear();
                    Console.ResetColor();

                    Console.ForegroundColor = ConsoleColor.White;
                    Console.Write($"G435 will approximately work until : {emptyBatteryDate}, ", ConsoleColor.White);

                    Console.ForegroundColor = ((int)timeLeft.TotalHours <= 4) ? ConsoleColor.Red : ConsoleColor.Green;
                    Console.Write($"({(int)timeLeft.TotalHours}h {timeLeft.Minutes}m {timeLeft.Seconds}s left)", ConsoleColor.Red);

                    Console.ForegroundColor = ConsoleColor.White;

                    Console.WriteLine();
                    Console.WriteLine("Press 'Ctrl+C' to quit...");

                    await Task.Delay(UPDATE_DELAY_MILLISECONDS);
                }
            }
            else
            {
                Console.WriteLine($"Error : option number {optionNumber} not found...");
                Console.WriteLine("Press 'Enter' to quit...");
                Console.ReadLine();
            }
        }

        public static DateTime GetSystemBootTime()
        {
            // Create a query for OS objects
            var query = new SelectQuery("Win32_OperatingSystem", "Status=\"OK\"");

            // Initialize an object searcher with this query
            var searcher = new ManagementObjectSearcher(query);
            string strLastBootTime = "";

            // Get the resulting collection and loop through it
            foreach (ManagementObject envVar in searcher.Get())
            {
                strLastBootTime = envVar["LastBootUpTime"].ToString();
            }

            return ManagementDateTimeConverter.ToDateTime(strLastBootTime);
        }

        public static DateTime GetLastSavedDateTime()
        {
            return LoadLastDateTime(CACHE_FILENAME);
        }

        public static DateTime GetCurrentTime()
        {
            return DateTime.Now;
        }

        public static DateTime GetApproximateEmptyBatteryDateTime(DateTime startTime, int hourUsage, int correctionPercentage, int batteryHealthPercentage)
        {
            float batteryHealthMultipiler = ((float)batteryHealthPercentage / 100.0f);
            float actualHourUsage = (hourUsage * batteryHealthMultipiler);

            int approximateHours = (int)((actualHourUsage * (float)correctionPercentage) / 100.0f);
            var approximateTimeSpan = new TimeSpan(approximateHours, 0, 0);

            return (startTime + approximateTimeSpan);
        }

        public static async Task<bool> SaveLastDateTime(DateTime dateTime, string filename)
        {
            string path = Path.GetFullPath(filename);

            var encoding = new UTF8Encoding();
            byte[] result = encoding.GetBytes(dateTime.ToUniversalTime().ToString("o"));

            bool writeToFileResult = false;

            using (FileStream SourceStream = File.Open(path, FileMode.OpenOrCreate))
            {
                try
                {
                    await SourceStream.WriteAsync(result, 0, result.Length);
                    writeToFileResult = true;
                }
                catch (Exception)
                {
                    writeToFileResult = false;
                }
            }

            return writeToFileResult;
        }

        public static DateTime LoadLastDateTime(string filename)
        {
            string path = Path.GetFullPath(filename);

            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Cache file not found...");
            }

            string result = "";

            using (FileStream fs = File.OpenRead(path))
            {
                byte[] b = new byte[1024];
                var temp = new UTF8Encoding(true);
                int readLen;

                while ((readLen = fs.Read(b, 0, b.Length)) > 0)
                {
                    result += temp.GetString(b, 0, readLen);
                }
            }

            return DateTime.Parse(result);
        }

        public static IniData LoadSetting()
        {
            var parser = new FileIniDataParser();
            string path = Path.GetFullPath(SETTING_FILENAME);

            if (!File.Exists(path))
            {
                IniData defaultSetting = new IniData();
                defaultSetting["Battery"]["batteryUsageHour"] = $"{DefaultBatteryUsageHours}";
                defaultSetting["Battery"]["batteryUsageCorrectionPercentage"] = $"{DefaultBatteryUsageHoursCorrectionPercentage}";
                defaultSetting["Battery"]["batteryHealthPercentage"] = $"{DefaultBatteryHealthPercentage}";
                parser.WriteFile(path, defaultSetting);
            }

            IniData setting = parser.ReadFile(path);
            return setting;
        }
    }
}

