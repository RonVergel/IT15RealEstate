using System.ComponentModel.DataAnnotations;

namespace RealEstateCRM.Attributes
{
    public class ConditionalRequiredAttribute : ValidationAttribute
    {
        private readonly string _dependentProperty;
        private readonly object _targetValue;

        public ConditionalRequiredAttribute(string dependentProperty, object targetValue)
        {
            _dependentProperty = dependentProperty;
            _targetValue = targetValue;
        }

        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            var instance = validationContext.ObjectInstance;
            var type = instance.GetType();
            var dependentPropertyInfo = type.GetProperty(_dependentProperty);

            if (dependentPropertyInfo == null)
            {
                return new ValidationResult($"Unknown property: {_dependentProperty}");
            }

            var dependentValue = dependentPropertyInfo.GetValue(instance, null);

            // If the dependent property matches the target value, then this field is required
            if (dependentValue?.ToString() == _targetValue?.ToString())
            {
                if (value == null || string.IsNullOrWhiteSpace(value.ToString()))
                {
                    return new ValidationResult(ErrorMessage ?? $"{validationContext.DisplayName} is required when {_dependentProperty} is {_targetValue}.");
                }
            }

            return ValidationResult.Success;
        }
    }
}