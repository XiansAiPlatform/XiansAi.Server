public class CertificateValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; } = new List<string>();

    public void AddError(string error)
    {
        Errors.Add(error);
        IsValid = false;
    }
} 