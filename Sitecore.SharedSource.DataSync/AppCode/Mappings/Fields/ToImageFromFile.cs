﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sitecore.Data.Items;
using Sitecore.SharedSource.Logger.Log;
using Sitecore.SharedSource.Logger.Log.Builder;

namespace Sitecore.SharedSource.DataSync.Mappings.Fields
{
    public class ToImageFromFile: ToImageFromUrl
    {
        private const string ImageFilesToImportAbsolutePathFieldName = "ImageFilesToImportAbsolutePath";
        private const string SearchTopDirectoryOnlyFieldName = "SearchTopDirectoryOnly";
        private string ImageFilesToImportAbsolutePath { get; set; }
        private SearchOption SearchTopDirectoryOnly { get; set; }

        public ToImageFromFile(Item i) : base(i)
        {
            ImageFilesToImportAbsolutePath = i[ImageFilesToImportAbsolutePathFieldName];
            SearchTopDirectoryOnly = i[SearchTopDirectoryOnlyFieldName] == "1" ? SearchOption.TopDirectoryOnly : SearchOption.AllDirectories;
        }
        
        public override byte[] GetImageAsBytes(Providers.BaseDataMap map, object importRow, ref Item newItem, string importValue, ref LevelLogger logger)
        {
            var getImageAsBytesLogger = logger.CreateLevelLogger();
            try
            {
                if (IsRequired && String.IsNullOrEmpty(importValue))
                {
                    getImageAsBytesLogger.AddError("Required field was null or empty in GetImageAsBytes", String.Format(
                            "The 'GetImageAsBytes' could not retrieve an image since the importValue was null or empty. The field was marked as required. " +
                            "ImportRow: {0}.",
                            map.GetImportRowDebugInfo(importRow)));
                    return null;
                }
                if (!IsRequired && String.IsNullOrEmpty(importValue))
                {
                    return null;
                }
                if (String.IsNullOrEmpty(ImageFilesToImportAbsolutePath))
                {
                    getImageAsBytesLogger.AddError("The setting 'ImageFilesToImportAbsolutePath' was null or empty", String.Format(
                            "The setting 'ImageFilesToImportAbsolutePath' was null or empty. Therefor the image could not be found, since there is no place to search for it. Plase provide a value." +
                            "ImportValue: {0}. ImportRow: {1}.", importValue, map.GetImportRowDebugInfo(importRow)));
                    return null;
                }
                var searchPattern = importValue + "*.*";
                var directoryInfo = new DirectoryInfo(ImageFilesToImportAbsolutePath);
                var files = directoryInfo.GetFiles(searchPattern, SearchTopDirectoryOnly);

                if (files.Length == 0)
                {
                    getImageAsBytesLogger.AddError("Attempt to find a file failed in GetImageAsBytes", String.Format(
                            "In the GetImageAsBytes method the attempt to find a file failed. No file was found in the search for file '{0}' in the folder '{1}'." +
                            " ImportValue: {2}. ImportRow: {3}. SearchTopDirectoryOnlye: {4}.", searchPattern,
                            ImageFilesToImportAbsolutePath, importValue, map.GetImportRowDebugInfo(importRow),
                            SearchTopDirectoryOnly));
                    return null;
                }
                if (files.Length > 1)
                {
                    getImageAsBytesLogger.AddError("More than one file found in GetImageAsBytes", String.Format(
                            "In the GetImageAsBytes method there where found more than one file in the search for file '{0}' in the folder '{1}'." +
                            " ImportValue: {2}. ImportRow: {3}. SearchTopDirectoryOnlye: {4}.", searchPattern,
                            ImageFilesToImportAbsolutePath, importValue, map.GetImportRowDebugInfo(importRow),
                            SearchTopDirectoryOnly));
                    return null;
                }
                var file = files.First();
                if (file != null)
                {
                    try
                    {
                        byte[] bytes = File.ReadAllBytes(file.FullName);
                        return bytes;
                    }
                    catch (Exception ex)
                    {
                        getImageAsBytesLogger.AddError("Exception occured trying to ReadAllBytes from GetImageAsBytes", String.Format(
                                "In the GetImageAsBytes method an exception occured in trying to ReadAllBytes from file '{0}'. SearchPattern: '{1}' in the folder '{2}'." +
                                " ImportValue: {3}. ImportRow: {4}. SearchTopDirectoryOnlye: {5}. Exception: {6}.",
                                file.FullName, searchPattern, ImageFilesToImportAbsolutePath, importValue,
                                map.GetImportRowDebugInfo(importRow), SearchTopDirectoryOnly,
                                map.GetExceptionDebugInfo(ex)));
                        return null;
                    }
                }
                getImageAsBytesLogger.AddError("One file found but it was null in GetImageAsBytes", String.Format(
                        "In the GetImageAsBytes method one file was found, but it was null. SearchPattern: '{0}' in the folder '{1}'." +
                        " ImportValue: {2}. ImportRow: {3}. SearchTopDirectoryOnlye: {4}.",
                        searchPattern, ImageFilesToImportAbsolutePath, importValue, map.GetImportRowDebugInfo(importRow),
                        SearchTopDirectoryOnly));
                return null;
            }
            catch (Exception ex)
            {
                getImageAsBytesLogger.AddError(CategoryConstants.GetImageAsBytesFailedWithException, String.Format("In the GetImageAsBytes method an exception occured. ImageFilesToImportAbsolutePath: '{0}'." +
                                              " ImportValue: {1}. ImportRow: {2}. SearchTopDirectoryOnlye: {3}. Exception: {4}", 
                                              ImageFilesToImportAbsolutePath, importValue, map.GetImportRowDebugInfo(importRow), SearchTopDirectoryOnly, map.GetExceptionDebugInfo(ex)));
                return null;
            }
        }
    }
}