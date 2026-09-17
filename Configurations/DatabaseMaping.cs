
namespace Master.Configuration;

public static class DatabaseMapping
{
    public static class States
    {
        public const string Table = "States";

        public const string Id = "Id";
        public const string StateName = "StateName";
        public const string EntryOn = "EntryOn";
        public const string UserId = "UserId";
        public const string IsActive = "IsActive";
    }

    public static class LoginUsers
    {
        public const string Table = "LoginUsers";

        public const string Id = "Id";
        public const string UserName = "UserName";
        public const string Password = "Password";
        public const string IsActive = "IsActive";
    }

    public static class ClientMaster
    {
        public const string Table = "ClientMaster";

        public const string Id = "Id";
        public const string ClientName = "ClientName";
        public const string MobileNo = "MobileNo";
        public const string Email = "Email";
        public const string Address = "Address";
        public const string City = "City";
        public const string EntryOn = "EntryOn";
        public const string UserId = "UserId";
        public const string StateId = "StateId";
        public const string IsActive = "IsActive";
    }

    public static class DailySupport
    {
        public const string Table = "DailySupport";

        public const string Id = "Id";
        public const string SupportDate = "SupportDate";
        public const string ClientId = "ClientId";
        public const string UserId = "UserId";
        public const string SupportType = "SupportType";
        public const string Subject = "Subject";
        public const string Description = "Description";
        public const string Status = "Status";
        public const string Priority = "Priority";
        public const string StartTime = "StartTime";
        public const string EndTime = "EndTime";
        public const string FollowUpDate = "FollowUpDate";
        public const string Remarks = "Remarks";
        public const string EntryOn = "EntryOn";
    }

    public static class ClientVisiting
    {
        public const string Table = "ClientVisiting";

        public const string Id = "Id";
        public const string VisitDate = "VisitDate";
        public const string ClientId = "ClientId";
        public const string UserId = "UserId";
        public const string VisitType = "VisitType";
        public const string PersonMet = "PersonMet";
        public const string Subject = "Subject";
        public const string Discussion = "Discussion";
        public const string Outcome = "Outcome";
        public const string NextAction = "NextAction";
        public const string FollowUpDate = "FollowUpDate";
        public const string Remarks = "Remarks";
        public const string Status = "Status";
        public const string EntryOn = "EntryOn";
        public const string PreviousMeetingId = "PreviousMeetingId";
    }
}

