namespace Core.DTOs
{
	public class ApiResponse<T>
	{
		public int Status { get; set; }
		public string Message { get; set; } = string.Empty;
		public T? Data { get; set; }
		public PagingInfo? Paging { get; set; }
		public object? Error { get; set; }

		public static ApiResponse<T> Success(T? data, string message = "Success", PagingInfo? paging = null)
		{
			return new ApiResponse<T>
			{
				Status = 200,
				Message = message,
				Data = data,
				Paging = paging,
				Error = null
			};
		}

		public static ApiResponse<T> SuccessWithStatus(int status, T? data, string message = "Success", PagingInfo? paging = null)
		{
			return new ApiResponse<T>
			{
				Status = status,
				Message = message,
				Data = data,
				Paging = paging,
				Error = null
			};
		}

		public static ApiResponse<T> Fail(int status, string message, object? error = null)
		{
			return new ApiResponse<T>
			{
				Status = status,
				Message = message,
				Data = default,
				Paging = null,
				Error = error
			};
		}
	}
}
