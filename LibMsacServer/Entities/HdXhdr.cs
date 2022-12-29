using System;
using System.Collections.Generic;
using System.Text;

namespace LibMsacServer.Entities
{
    public class HdXhdr
    {
        public string MimeType { get; set; }
        public bool Trigger { get; set; }
        public bool BlankScreen { get; set; }
        public bool FlushMemory { get; set; }
        public int? LotId { get; set; }
    }
}
