using System;
using System.Collections.Generic;
using System.Text;

namespace LibMsacServer.Entities
{
    public class HdOutgoingImage
    {
        public string DataServiceName { get; set; }
        public string FileName { get; set; }
        public int LotId { get; set; }
        public string UniqueTag { get; set; }
        public int Duration { get; set; }

        public static string CreateUniqueTag(DateTime time)
        {
            return time.ToString("ddd MM dd HH:mm:ss:fff K yyyy");
        }
    }
}
