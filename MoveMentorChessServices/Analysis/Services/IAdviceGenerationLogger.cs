namespace MoveMentorChessServices;

public interface IAdviceGenerationLogger
{
    void Record(AdviceGenerationTrace trace);
}
