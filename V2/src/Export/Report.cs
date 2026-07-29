using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using SAUDICO.Federate.Core;

namespace SAUDICO.Federate.Export
{
    public static class Report
    {
        public static string Write(string directory, IEnumerable<Job> jobs)
        {
            string path = Path.Combine(
                directory,
                "SAUDICO-Federation-Report-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv");

            using (StreamWriter writer = new StreamWriter(path, false, new UTF8Encoding(true)))
            {
                writer.WriteLine(
                    "Source RVT,Output NWC,Start,End,Duration,Status,Coordinates,Detail Level,Parameters,Warnings,Errors");

                foreach (Job job in jobs)
                {
                    string[] fields =
                    {
                        Csv(job.Source),
                        Csv(job.Output),
                        Csv(job.Start.ToString("O")),
                        Csv(job.End.ToString("O")),
                        Csv((job.End - job.Start).TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)),
                        Csv(job.State.ToString()),
                        Csv(job.Settings.Coordinates.ToString()),
                        Csv(job.Settings.Detail.ToString()),
                        Csv(job.Settings.Parameters.ToString()),
                        Csv(job.Warnings),
                        Csv(job.Error)
                    };

                    writer.WriteLine(string.Join(",", fields));
                }
            }

            return path;
        }

        private static string Csv(object? value)
        {
            string text = value == null ? string.Empty : value.ToString();
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }
    }
}
