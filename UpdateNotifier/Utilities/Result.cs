namespace UpdateNotifier.Utilities;

public class Result<TSource>(ResultStatus status, TSource item, Exception? exception = null)
{
	public readonly Exception?   Exception = exception;
	public readonly TSource?     Item      = item;
	public readonly ResultStatus Status    = status;

	public bool IsSuccess => Status == ResultStatus.Success;

	public void Deconstruct(out Exception? exception, out ResultStatus resultStatus)
	{
		exception = Exception;
		resultStatus = Status;
	}

	public override string ToString()
		=> Exception switch
		{
			{ }  => $"{nameof(Status)}: {Status}\n{Exception}: {nameof(Exception)}",
			null => $"{nameof(Status)}: {Status}",
		};

	public static Result<TSource?> Failure(Exception? exception = null, TSource? resultItem = default) => new(ResultStatus.Failure, resultItem, exception);

	public static Result<TSource> Success(TSource resultItem) => new(ResultStatus.Success, resultItem);
}

public enum ResultStatus { Success, Failure }