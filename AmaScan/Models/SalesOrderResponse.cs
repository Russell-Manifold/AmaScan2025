using System;
using System.Collections.Generic;

namespace Data.Model
{
    public class SalesOrderResponse
    {
        public int Id { get; set; }
        public string? CustomerAccount { get; set; }
        public string? CustomerName { get; set; }
        public string? CustomerOrderNo { get; set; }
        public string? AreaDescription { get; set; }
        public DateTime DueDate { get; set; }
        public string? OrderStatus { get; set; }
        public string? Reference { get; set; }
        public List<SalesOrderLine>? Lines { get; set; }

        // Workflow Tracking at Header Level
        public string? Picker { get; set; }
        public bool PickStarted { get; set; }
        public bool Picked { get; set; }
        public bool Packed { get; set; }
        public bool Checked { get; set; }
        public bool Authed { get; set; }
        public double? Sequence { get; set; }

        // Picking Phase Tracking
        public DateTime? PickStartDateTime { get; set; }
        public DateTime? PickCompleteDateTime { get; set; }
        public string? PickedBy { get; set; }

        // Packing Phase Tracking
        public DateTime? PackStartDateTime { get; set; }
        public DateTime? PackCompleteDateTime { get; set; }
        public string? PackedBy { get; set; }

        // Checking Phase Tracking
        public DateTime? CheckStartDateTime { get; set; }
        public DateTime? CheckCompleteDateTime { get; set; }
        public string? CheckedBy { get; set; }

        // Authorization Phase Tracking
        public DateTime? AuthStartDateTime { get; set; }
        public DateTime? AuthCompleteDateTime { get; set; }
        public string? AuthorizedBy { get; set; }
    }
}