﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using ServiceStack.FluentValidation;
using ServiceStack.FluentValidation.Results;
using ServiceStack.Web;

namespace ServiceStack.Validation
{
    public static class ValidationFilters
    {
        public static async Task RequestFilterAsync(IRequest req, IResponse res, object requestDto)
        {
            await RequestFilterAsync(req, res, requestDto, true);
        }

        public static async Task RequestFilterAsyncIgnoreWarningsInfo(IRequest req, IResponse res, object requestDto)
        {
            await RequestFilterAsync(req, res, requestDto, false);
        }

        private static async Task RequestFilterAsync(IRequest req, IResponse res, object requestDto,
            bool treatInfoAndWarningsAsErrors)
        {
            var validator = ValidatorCache.GetValidator(req, requestDto.GetType());
            if (validator == null)
                return;

            using (validator as IDisposable)
            {
                try
                {
                    var validationResult = await validator.ValidateAsync(req, requestDto);
    
                    if (treatInfoAndWarningsAsErrors && validationResult.IsValid)
                        return;
    
                    if (!treatInfoAndWarningsAsErrors &&
                        (validationResult.IsValid || validationResult.Errors.All(v => v.Severity != Severity.Error)))
                        return;
    
                    var errorResponse =
                        await HostContext.RaiseServiceException(req, requestDto, validationResult.ToException())
                        ?? DtoUtils.CreateErrorResponse(requestDto, validationResult.ToErrorResult());
   
                    var autoBatchIndex = req.GetItem(Keywords.AutoBatchIndex)?.ToString();
                    if (autoBatchIndex != null)
                    {
                        var responseStatus = errorResponse.GetResponseStatus();
                        if (responseStatus != null)
                        {
                            if (responseStatus.Meta == null)
                                responseStatus.Meta = new Dictionary<string, string>();
                            responseStatus.Meta[Keywords.AutoBatchIndex] = autoBatchIndex;
                        }
                    }
                    
                    var validationFeature = HostContext.GetPlugin<ValidationFeature>();
                    if (validationFeature?.ErrorResponseFilter != null)
                    {
                        errorResponse = validationFeature.ErrorResponseFilter(req, validationResult, errorResponse);
                    }
    
                    await res.WriteToResponse(req, errorResponse);
                }
                catch (Exception ex)
                {
                    var validationEx = ex.UnwrapIfSingleException();
    
                    var errorResponse = await HostContext.RaiseServiceException(req, requestDto, validationEx)
                                        ?? DtoUtils.CreateErrorResponse(requestDto, validationEx);
    
                    await res.WriteToResponse(req, errorResponse);
                }
            }
        }

        public static async Task ResponseFilterAsync(IRequest req, IResponse res, object requestDto)
        {
            if (!(requestDto is IHasResponseStatus response))
                return;

            var validator = ValidatorCache.GetValidator(req, req.Dto.GetType());
            if (validator == null)
                return;

            var validationResult = await ValidateAsync(validator, req, req.Dto);

            if (!validationResult.IsValid)
            {
                var responseStatus = response.ResponseStatus
                     ?? DtoUtils.CreateResponseStatus(validationResult.Errors[0].ErrorCode);
                foreach (var error in validationResult.Errors)
                {
                    var responseError = new ResponseError
                    {
                        ErrorCode = error.ErrorCode,
                        FieldName = error.PropertyName,
                        Message = error.ErrorMessage,
                        Meta = new Dictionary<string, string> {["Severity"] = error.Severity.ToString()}
                    };
                    responseStatus.Errors.Add(responseError);
                }

                response.ResponseStatus = responseStatus;
            }
        }

        public static async Task<ValidationResult> ValidateAsync(this IValidator validator, IRequest req, object requestDto)
        {
            if (validator == null)
                throw new ArgumentNullException(nameof(validator));
            if (req == null)
                throw new ArgumentNullException(nameof(req));
            if (requestDto == null)
                throw new ArgumentNullException(nameof(requestDto));
            
            var ruleSet = req.Verb;
            using (validator as IDisposable)
            {
                var validationContext = new ValidationContext(requestDto, null, 
                    new MultiRuleSetValidatorSelector(ruleSet)) {
                    Request = req
                };
                
                if (validator.HasAsyncValidators(validationContext,ruleSet))
                {
                    return await validator.ValidateAsync(validationContext);
                }

                return validator.Validate(validationContext);
            }
        }

        public static ValidationResult Validate(this IValidator validator, IRequest req, object requestDto)
        {
            if (validator == null)
                throw new ArgumentNullException(nameof(validator));
            if (req == null)
                throw new ArgumentNullException(nameof(req));
            if (requestDto == null)
                throw new ArgumentNullException(nameof(requestDto));
            
            var ruleSet = req.Verb;
            using (validator as IDisposable)
            {
                var validationContext = new ValidationContext(requestDto, null, 
                    new MultiRuleSetValidatorSelector(ruleSet)) {
                    Request = req
                };
                
                if (validator.HasAsyncValidators(validationContext,ruleSet))
                    throw new NotSupportedException($"Use {nameof(ValidateAsync)} to call async validator '{validator.GetType().Name}'");

                return validator.Validate(validationContext);
            }
        }

    }
}