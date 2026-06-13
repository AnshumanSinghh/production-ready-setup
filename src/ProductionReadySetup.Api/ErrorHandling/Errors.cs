namespace ProductionReadySetup.Api.ErrorHandling
{
    public static class Errors
    {
        public static class Common
        {
            public static readonly ErrorDescriptor BadRequest = ErrorFactory.Create(
                "common.bad_request",
                "Bad request.",
                StatusCodes.Status400BadRequest);

            public static readonly ErrorDescriptor InvalidRequest = ErrorFactory.Create(
                "common.invalid_request",
                "Invalid request.",
                StatusCodes.Status400BadRequest);

            public static readonly ErrorDescriptor ValidationFailed = ErrorFactory.Create(
                "common.validation_failed",
                "Validation failed.",
                StatusCodes.Status422UnprocessableEntity);

            public static readonly ErrorDescriptor Unexpected = ErrorFactory.Create(
                "common.unexpected_error",
                "An unexpected error occurred.",
                StatusCodes.Status500InternalServerError);
        }

        public static class Product
        {
            public static readonly ErrorDescriptor NotFound = ErrorFactory.Create(
                "products.not_found",
                "Product not found.",
                StatusCodes.Status404NotFound);

            public static readonly ErrorDescriptor DuplicateName = ErrorFactory.Create(
                "products.duplicate_name",
                "Product already exists.",
                StatusCodes.Status409Conflict);
        }
    }
}
