namespace CMCS.Models
{
    public enum UserRole
    {
        LECTURER,
        PROGRAMME_COORDINATOR,
        ACADEMIC_MANAGER,
        HR,
        ADMIN
    }

    public enum ClaimStatus
    {
        PENDING,
        UNDER_REVIEW,
        APPROVED_PC,
        APPROVED_AM,
        APPROVED_FINAL,
        REJECTED,
        PAID,
        CANCELLED
    }

    public enum ApprovalLevel
    {
        PROGRAMME_COORDINATOR,
        ACADEMIC_MANAGER,
        HR
    }

    public enum ApprovalStatus
    {
        PENDING,
        APPROVED,
        REJECTED,
        PENDING_CLARIFICATION
    }
}
