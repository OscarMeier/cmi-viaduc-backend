﻿using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using CMI.Contract.DocumentConverter;
using MassTransit;
using Rebex;
using Rebex.Net;
using Serilog;

namespace CMI.Engine.Asset
{
    public class TextEngine : ITextEngine
    {
        private readonly IRequestClient<ExtractionStartRequest, ExtractionStartResult> extractionRequestClient;
        private readonly IRequestClient<JobInitRequest, JobInitResult> jobRequestClient;
        private readonly IRequestClient<SupportedFileTypesRequest, SupportedFileTypesResponse> supportedFileTypesRequestClient;
        private readonly string sftpLicenseKey;
        private string[] supportedFileTypes;

        public TextEngine(IRequestClient<JobInitRequest, JobInitResult> jobRequestClient,
            IRequestClient<ExtractionStartRequest, ExtractionStartResult> extractionRequestClient,
            IRequestClient<SupportedFileTypesRequest, SupportedFileTypesResponse> supportedFileTypesRequestClient,
            string sftpLicenseKey)
        {
            this.jobRequestClient = jobRequestClient;
            this.extractionRequestClient = extractionRequestClient;
            this.supportedFileTypesRequestClient = supportedFileTypesRequestClient;
            this.sftpLicenseKey = sftpLicenseKey;
        }

        public async Task<string> ExtractText(string file)
        {
            var fi = new FileInfo(file);

            try
            {
                var stopWatch = new Stopwatch();
                stopWatch.Start();

                var conversionSettings = new JobInitRequest
                {
                    FileNameWithExtension = fi.Name,
                    RequestedProcessType = ProcessType.TextExtraction
                };

                var registrationResponse = await jobRequestClient.Request(conversionSettings);
                Log.Information("Successfully registered job for text extraction of file {Name}. Got job id {JobId}", fi.Name,
                    registrationResponse.JobGuid);

                await UploadFile(registrationResponse, fi);
                Log.Information("File '{Name}' uploaded", fi.Name);

                var requestSettings = new ExtractionStartRequest
                {
                    JobGuid = registrationResponse.JobGuid
                };
                Log.Information("Sent actual text extraction request for job id {jobGuid}", registrationResponse.JobGuid);

                var extractionResult = await extractionRequestClient.Request(requestSettings);

                if (extractionResult.IsInvalid)
                {
                    throw new Exception(extractionResult.ErrorMessage);
                }

                var lengthInBytes = string.IsNullOrEmpty(extractionResult.Text) ? 0 : extractionResult.Text.Length;
                Log.Information("Retrieved content for file {Name} in {ElapsedMilliseconds} ms. Length of content is {lengthInBytes} bytes.", fi.Name,
                    stopWatch.ElapsedMilliseconds,
                    lengthInBytes);

                return extractionResult.Text;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Unexpected error while extracting text for file {FullName}", fi.FullName);
                throw;
            }
            finally
            {
                if (fi.Exists)
                {
                    fi.Delete();
                    fi.Refresh();
                    if (fi.Exists)
                    {
                        Log.Warning($"Unable to delete file '{fi.FullName}'");
                    }
                }
            }
        }


        public async Task<string[]> GetSupportedFileTypes()
        {
            if (supportedFileTypes != null && supportedFileTypes.Length > 0)
            {
                return supportedFileTypes;
            }

            var request = new SupportedFileTypesRequest
            {
                ProcessType = ProcessType.TextExtraction
            };

            var response = await supportedFileTypesRequestClient.Request(request);
            supportedFileTypes = response.SupportedFileTypes;
            return supportedFileTypes;
        }

        private async Task UploadFile(JobInitResult jobInitResult, FileInfo toBeConverted)
        {
            Licensing.Key = sftpLicenseKey;
            using (var client = new Sftp())
            {
                await client.ConnectAsync(jobInitResult.UploadUrl, jobInitResult.Port);
                await client.LoginAsync(jobInitResult.User, jobInitResult.Password);

                var uploadName = $"{client.GetCurrentDirectory()}{toBeConverted.Name}";
                Log.Verbose("Uploading file {FullName} to {UploadName}", toBeConverted.FullName, uploadName);
                if (toBeConverted.Exists)
                {
                    await client.PutFileAsync(toBeConverted.FullName, uploadName);
                    Log.Verbose("Upload successfull");
                }
                else
                {
                    Log.Verbose("File {FullName} does not exist, or cannot be accessed.", toBeConverted.FullName);
                    throw new FileNotFoundException("File could not be uploaded, because source file is not accessible or cannot be found",
                        toBeConverted.FullName);
                }
            }
        }
    }
}