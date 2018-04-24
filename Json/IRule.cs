namespace Json
{
    public interface IRule
    {
        bool Matches(JsonObject obj);
    }
}
