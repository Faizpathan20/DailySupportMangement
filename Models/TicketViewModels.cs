namespace Master.Models;

// ============================================
// TICKETING SYSTEM — ViewModels
// Daily Support ko ek professional ticketing
// system banane ke liye saare models ek jagah.
// ============================================

// --------------------------------------------
// Ek master dropdown ka ek option (Id + Name).
// --------------------------------------------
public class TicketOption
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    // Sirf SubCategory ke liye: parent Category id.
    public int ParentId { get; set; }

    // Sirf Priority ke liye: SLA hours.
    public int SlaHours { get; set; }

    // Sirf Clients ke liye: mobile number.
    public string Mobile { get; set; } = string.Empty;
}

// --------------------------------------------
// Create Ticket page ka model.
// --------------------------------------------
public class TicketCreateViewModel
{
    // Auto-generated (readonly dikhega)
    public string TicketNo { get; set; } = string.Empty;

    // Aaj ki date (server IST)
    public DateTime SupportDate { get; set; } = DateTime.Today;

    // ---- Dropdown options ----
    public List<TicketOption> Channels { get; set; } = new();
    public List<TicketOption> CallTypes { get; set; } = new();
    public List<TicketOption> Categories { get; set; } = new();
    public List<TicketOption> SubCategories { get; set; } = new();
    public List<TicketOption> Priorities { get; set; } = new();
    public List<TicketOption> Statuses { get; set; } = new();
    public List<TicketOption> Users { get; set; } = new();

    // Clients (mobile search ke liye) — Id, Name, Mobile
    public List<TicketOption> Clients { get; set; } = new();

    // ---- Selected values (post ke baad refill) ----
    public int? ClientId { get; set; }
    public string Mobile { get; set; } = string.Empty;
    public string CallerName { get; set; } = string.Empty;
    public int? ChannelId { get; set; }
    public int? CallTypeId { get; set; }
    public int? CategoryId { get; set; }
    public int? SubCategoryId { get; set; }
    public int? PriorityId { get; set; }
    public int? StatusId { get; set; }
    public int? AssignedTo { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    // Default status (Open) — form me preset.
    public int DefaultStatusId { get; set; }

    // Logged-in user (Assign To default).
    public int? CurrentUserId { get; set; }
}

// --------------------------------------------
// Ticket Detail page ka model.
// --------------------------------------------
public class TicketDetailViewModel
{
    // ---- Ticket header ----
    public int Id { get; set; }
    public string TicketNo { get; set; } = string.Empty;
    public DateTime SupportDate { get; set; }
    public DateTime? SlaDue { get; set; }
    public DateTime? ResolvedOn { get; set; }
    public DateTime? ClosedOn { get; set; }
    public int TimeSpentMinutes { get; set; }

    // ---- Client ----
    public int ClientId { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public string Mobile { get; set; } = string.Empty;
    public string CallerName { get; set; } = string.Empty;

    // ---- Issue ----
    public string Subject { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    // ---- Master values (name + id) ----
    public int? ChannelId { get; set; }
    public string ChannelName { get; set; } = string.Empty;
    public int? CallTypeId { get; set; }
    public string CallTypeName { get; set; } = string.Empty;
    public int? CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public int? SubCategoryId { get; set; }
    public string SubCategoryName { get; set; } = string.Empty;

    public int StatusId { get; set; }
    public string StatusName { get; set; } = string.Empty;
    public bool StatusIsClosed { get; set; }

    public int? PriorityId { get; set; }
    public string PriorityName { get; set; } = string.Empty;
    public int SlaHours { get; set; }

    // ---- Assignment ----
    public int? AssignedTo { get; set; }
    public string AssignedToName { get; set; } = string.Empty;
    public int? CreatedBy { get; set; }
    public string CreatedByName { get; set; } = string.Empty;

    // ---- Permissions (computed) ----
    public bool CanAddWork { get; set; }
    public bool CanAssign { get; set; }
    public bool IsReadOnly { get; set; }

    // Resolved ticket ko owner (ya admin) close kar sakta hai.
    public bool CanClose { get; set; }

    // ---- Review ----
    public string ReviewStatus { get; set; } = string.Empty;
    public int? ReviewedBy { get; set; }
    public string ReviewedByName { get; set; } = string.Empty;
    public DateTime? ReviewedAt { get; set; }

    // Admin? -> review panel (accept/reject) dikhta hai.
    public bool IsAdmin { get; set; }

    // ---- Timeline (updates + history merged) ----
    public List<TicketTimelineItem> Timeline { get; set; } = new();

    // ---- Work updates (separate, agar chahiye) ----
    public List<TicketUpdateItem> Updates { get; set; } = new();

    // ---- Reviews history ----
    public List<TicketReviewItem> Reviews { get; set; } = new();

    // ---- Client ke purane tickets ----
    public List<TicketListItem> ClientTickets { get; set; } = new();

    // ---- Client summary (total / open / closed) ----
    public int ClientTotalTickets { get; set; }

    public int ClientOpenTickets { get; set; }

    public int ClientClosedTickets { get; set; }

    // ---- Dropdowns (Assign + Status ke liye) ----
    public List<TicketOption> Users { get; set; } = new();

    public List<TicketOption> StatusOptions { get; set; } = new();

    // ---- Permissions ----
    public bool CanReview { get; set; }

    // ---- Derived helpers ----
    public bool IsClosed => StatusIsClosed;

    // NOTE: Mst_Status me "Resolved" ka IsClosed flag DB me 1 hai
    // (galat data). Isliye read-only / close jaise rules ke liye hum
    // flag ko chhod kar status ke *naam* se decide karte hain.
    public bool IsClosedByName =>
        string.Equals(
            StatusName,
            "Closed",
            StringComparison.OrdinalIgnoreCase);

    public bool IsResolvedByName =>
        string.Equals(
            StatusName,
            "Resolved",
            StringComparison.OrdinalIgnoreCase);

    public bool IsPendingReview =>
        string.Equals(
            StatusName,
            "Pending Review",
            StringComparison.OrdinalIgnoreCase);

    public bool IsOverdue =>
        SlaDue.HasValue
        && !IsClosedByName
        && SlaDue.Value < DateTime.UtcNow.AddMinutes(330);

    public string TimeSpentDisplay
    {
        get
        {
            if (TimeSpentMinutes <= 0)
            {
                return "-";
            }

            int h = TimeSpentMinutes / 60;
            int m = TimeSpentMinutes % 60;

            if (h == 0)
            {
                return $"{m}m";
            }

            return m == 0 ? $"{h}h" : $"{h}h {m}m";
        }
    }
}

// --------------------------------------------
// Timeline ka ek item (update ya history).
// --------------------------------------------
public class TicketTimelineItem
{
    // "Update" | "History" | "Review"
    public string Kind { get; set; } = "History";

    // Filter key: work | status | assign | created
    public string KindKey { get; set; } = "work";

    // Icon (emoji) jo timeline me dikhega.
    public string Icon { get; set; } = "\U0001F4DD";

    public DateTime At { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    // Kitne minute lage (Update ke liye).
    public int? TimeSpent { get; set; }

    // Next action (Update ke liye).
    public string NextAction { get; set; } = string.Empty;

    // "Waiting on" reason (jab status On Hold ho).
    public string WaitingOn { get; set; } = string.Empty;

    // Resolution note (jab status Resolved ho).
    public string ResolutionNote { get; set; } = string.Empty;

    // Agla follow-up date (optional).
    public DateTime? FollowUpDate { get; set; }

    // Kis din ka kaam tha (Update ke liye).
    public DateTime? WorkDate { get; set; }

    // Status / assign change: purana + naya value.
    public string OldStatus { get; set; } = string.Empty;

    public string NewStatus { get; set; } = string.Empty;

    // Kisne kiya.
    public string ByName { get; set; } = string.Empty;

    // Rating (Review ke liye).
    public int? Rating { get; set; }
}

// --------------------------------------------
// Ek work update row.
// --------------------------------------------
public class TicketUpdateItem
{
    public int UpdateId { get; set; }
    public DateTime WorkDate { get; set; }
    public string WorkDone { get; set; } = string.Empty;
    public int? TimeSpent { get; set; }
    public string NextAction { get; set; } = string.Empty;
    public string WaitingOn { get; set; } = string.Empty;
    public string ResolutionNote { get; set; } = string.Empty;
    public DateTime? FollowUpDate { get; set; }
    public string UpdatedByName { get; set; } = string.Empty;
    public DateTime EntryOn { get; set; }
}

// --------------------------------------------
// Ek review row (boss ka decision).
// --------------------------------------------
public class TicketReviewItem
{
    public int ReviewId { get; set; }
    public string ReviewedByName { get; set; } = string.Empty;
    public string ReviewAction { get; set; } = string.Empty;
    public int? Rating { get; set; }
    public string Remarks { get; set; } = string.Empty;
    public DateTime ReviewedAt { get; set; }
}

// --------------------------------------------
// Ek in-app notification (review aaya / approve / reject).
// --------------------------------------------
public class TicketNotification
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int? TicketId { get; set; }
    public string TicketNo { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;

    // review | approved | rejected
    public string Kind { get; set; } = string.Empty;

    public int? Rating { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}

// --------------------------------------------
// List page ke ek row (client history me bhi use).
// --------------------------------------------
public class TicketListItem
{
    public int Id { get; set; }
    public string TicketNo { get; set; } = string.Empty;
    public DateTime SupportDate { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string StatusName { get; set; } = string.Empty;
    public string PriorityName { get; set; } = string.Empty;
    public string AssignedToName { get; set; } = string.Empty;
    public string CreatedByName { get; set; } = string.Empty;

    // Classification (client tickets table ke liye).
    public string CategoryName { get; set; } = string.Empty;
    public string SubCategoryName { get; set; } = string.Empty;
    public string ChannelName { get; set; } = string.Empty;

    // Time spent in minutes.
    public int TimeSpentMinutes { get; set; }

    public DateTime? SlaDue { get; set; }
    public bool IsClosed { get; set; }

    // Review pill ke liye: "pending" | "approved" | "rejected" | "".
    public string ReviewPill { get; set; } = string.Empty;

    public bool IsOverdue =>
        SlaDue.HasValue
        && !IsClosed
        && SlaDue.Value < DateTime.UtcNow.AddMinutes(330);

    // "2h 15m" style display.
    public string TimeSpentDisplay
    {
        get
        {
            if (TimeSpentMinutes <= 0) return "-";

            int h = TimeSpentMinutes / 60;
            int m = TimeSpentMinutes % 60;

            if (h == 0) return $"{m}m";
            return m == 0 ? $"{h}h" : $"{h}h {m}m";
        }
    }

    // Status badge CSS ke liye (lowercase, space hata ke).
    public string StatusKey =>
        string.IsNullOrWhiteSpace(StatusName)
            ? "default"
            : StatusName.ToLower().Replace(" ", "");

    public string PriorityKey =>
        string.IsNullOrWhiteSpace(PriorityName)
            ? "default"
            : PriorityName.ToLower().Replace(" ", "");
}
