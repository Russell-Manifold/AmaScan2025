using System;
using System.Collections.Generic;
using System.Text;

namespace AmaScan.Models
{
    public class SalesHeaderUpdateRequest
    {
        public string Reference { get; set; }
        public string Picker { get; set; }
        public int? Sequence { get; set; }
        public bool? Issued { get; set; }
        public DateTime? IssuedDate { get; set; }
        public int? IssuedBy { get; set; }
        public bool? Ignore { get; set; }
        public DateTime? IgnoreDate { get; set; }
        public int? IgnoreBy { get; set; }

        // Scanner workflow fields - for updating picking status from scanner app
        public bool? PickStarted { get; set; }
        public bool? Picked { get; set; }
        public bool? Packed { get; set; }
        public bool? Checked { get; set; }
        public bool? Authed { get; set; }
    }

}
