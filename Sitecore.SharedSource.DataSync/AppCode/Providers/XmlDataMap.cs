﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Net;
using System.Xml;
using System.Xml.XPath;
using BackPack.Modules.AppCode.Import.Utility;
using Sitecore.Data.Items;
using Sitecore.Data;
using System.Data;
using System.Data.SqlClient;
using Sitecore.SharedSource.Logger.Log;
using Sitecore.SharedSource.Logger.Log.Builder;


namespace Sitecore.SharedSource.DataSync.Providers
{
	public class XmlDataMap : BaseDataMap {
	    private const string Dot = ".";
        private const string UrlPrefix = "http";
	    public string RawData { get; set; }

	    #region Properties

	    private readonly int DebugImportRowXmlCharacterLength = 50;

	    public Dictionary<string, string> DataSourceCache { get; set; }

	    #endregion Properties

		#region Constructor

        public XmlDataMap(Database db, Item importItem, LevelLogger logger)
            : base(db, importItem, logger)
        {
            Data = importItem[FieldNameData];
            if (string.IsNullOrEmpty(Query))
            {
                Logger.AddError("Error", "the 'Query' field was not set");
            }
		}
		
		#endregion Constructor

        #region Override Methods
        
        public override IList<object> GetImportData()
        {
            var xmlData = String.Empty;
            if (!String.IsNullOrEmpty(RawData))
            {
                xmlData = RawData;
            }
            else
            {
                RawData = !String.IsNullOrEmpty(Data)
                                  ? Data
                                  : XMLFileData;
                xmlData = RawData;
            }
            if (!String.IsNullOrEmpty(xmlData))
            {
                var textReader = new StringReader(xmlData);
                var settings = new XmlReaderSettings { ProhibitDtd = false, XmlResolver = null};
                using (XmlReader xmlReader = XmlReader.Create(textReader, settings))
                {
                    var xDocument = XDocument.Load(xmlReader);
                    return ExecuteXPathQuery(xDocument);
                }
            }
            Logger.AddError("Error", "No Import Data was retrieved from the method GetImportData. Please verify if the field 'Data', or 'Data Source' is filled out.");
            return null;
	    }

        /// <summary>
        /// gets custom data from a DataRow
        /// </summary>
        /// <param name="importRow"></param>
        /// <param name="fieldName"></param>
        /// <param name="errorMessage"></param>
        /// <returns></returns>
        public override string GetFieldValue(object importRow, string fieldName, ref LevelLogger logger)
        {
            var getFieldValueLogger = logger.CreateLevelLogger();
            try
            {
                var xElement = importRow as XElement;
                if (xElement != null)
                {
                    if (!String.IsNullOrEmpty(fieldName))
                    {
                        try
                        {
                            // First retrieve the fieldName as an attribute
                            var attribute = xElement.Attribute(fieldName);
                            if (attribute != null)
                            {
                                string value = attribute.Value;
                                if (!String.IsNullOrEmpty(value))
                                {
                                    return value;
                                }
                            }
                            else
                            {
                                // Then retrieve the fieldname as an subelement
                                var subElements = xElement.Elements(fieldName);
                                var elementsList = subElements.ToList();
                                if (elementsList.Count() > 1)
                                {
                                    // Log eror since document format is wrong. Has two or more elements with same name.
                                    getFieldValueLogger.AddError("Found more than one subelement with FieldName", String.Format(
                                            "The GetFieldValue method failed because the fieldName '{0}' resulted in more than one subelement in the Import Row. FieldName: {0}. ImportRow: {1}.",
                                            fieldName, GetImportRowDebugInfo(importRow)));
                                }
                                else if (elementsList.Count() == 1)
                                {
                                    var subElement = elementsList.First();
                                    if (subElement != null)
                                    {
                                        var value = subElement.Value;
                                        if (!String.IsNullOrEmpty(value))
                                        {
                                            return value;
                                        }
                                    }
                                }
                            }
                        }
                        catch (XmlException)
                        {
                            // We do nothing since this is most likely because we have a xpath query as the fieldname.
                        }

                        // Now finally try to retrieve through a xPath query
                        var executeXPathLogger = getFieldValueLogger.CreateLevelLogger();
                        var result = ExecuteXPathQueryOnXElement(xElement, fieldName, ref executeXPathLogger);
                        if (executeXPathLogger.HasErrors())
                        {
                            executeXPathLogger.AddError("Failure in ExecuteXPathQueryOnXElement", String.Format("The GetFieldValue method failed in executing the ExecuteXPathQueryOnXElement method."));
                        }
                        string fieldValue;
                        if (result is string)
                        {
                            return result as string;
                        }
                        var enumerable = result as IList<object> ?? result.Cast<object>().ToList();
                        if (TryParseAttribute(enumerable, out fieldValue, ref getFieldValueLogger))
                        {
                            return fieldValue;
                        }
                        if (TryParseElement(enumerable, out fieldValue, ref getFieldValueLogger))
                        {
                            return fieldValue;
                        }
                    }
                    else
                    {
                        getFieldValueLogger.AddError(CategoryConstants.TheFieldnameArgumentWasNullOrEmpty, String.Format("The GetFieldValue method failed because the 'fieldName' was null or empty. FieldName: {0}. ImportRow: {1}.", fieldName, GetImportRowDebugInfo(importRow)));
                    }
                }
                else
                {
                    getFieldValueLogger.AddError(CategoryConstants.TheImportRowWasNull, String.Format("The GetFieldValue method failed because the Import Row was null. FieldName: {0}.", fieldName));
                }
            }
            catch (Exception ex)
            {
                getFieldValueLogger.AddError(CategoryConstants.GetFieldValueFailed, String.Format("The GetFieldValue method failed with an exception. ImportRow: {0}. FieldName: {1}. Exception: {2}.", GetImportRowDebugInfo(importRow), fieldName, ex));
            }
            return String.Empty;
        }

	    private bool TryParseAttribute(IEnumerable result, out string fieldValue, ref LevelLogger logger)
        {
            fieldValue = String.Empty;
	        try
	        {
	            var xAttributes = result.Cast<XAttribute>();
	            var attributes = xAttributes as IList<XAttribute> ?? xAttributes.ToList();
	            if (attributes.Count() > 1)
	            {
	                string pipeseperatedValues = String.Empty;
                    foreach (var attribute in attributes)
                    {
                        if (attribute != null)
                        {
                            var value = attribute.Value;
                            if (!String.IsNullOrEmpty(value))
                            {
                                pipeseperatedValues += value + "|";
                            }
                        }
                    }
                    if (pipeseperatedValues.EndsWith("|"))
                    {
                        pipeseperatedValues = pipeseperatedValues.TrimEnd('|');
                    }
                    fieldValue = pipeseperatedValues;
                    return true;
	            }
	            if (attributes.Count() == 1)
	            {
	                var xAttribute = attributes.First();
	                if (xAttribute != null)
	                {
	                    fieldValue = xAttribute.Value;
	                    return true;
	                }
	            }
	        }
	        catch (Exception exception)
	        {
                return false;
	        }
	        return false;
	    }

        private bool TryParseElement(IEnumerable result, out string fieldValue, ref LevelLogger logger)
        {
            fieldValue = String.Empty;
            try
            {
                var xElements = result.Cast<XElement>();
                var elements = xElements as IList<XElement> ?? xElements.ToList();
                if (elements.Count() > 1)
                {
                    string pipeseperatedValues = String.Empty;
                    foreach (var element in elements)
                    {
                        if (element != null)
                        {
                            var value = element.Value;
                            if (!String.IsNullOrEmpty(value))
                            {
                                pipeseperatedValues += value + "|";
                            }
                        }
                    }
                    if (pipeseperatedValues.EndsWith("|"))
                    {
                        pipeseperatedValues = pipeseperatedValues.TrimEnd('|');
                    }
                    fieldValue = pipeseperatedValues;
                    return true;
                }
                if (elements.Count() == 1)
                {
                    var xElement = elements.First();
                    if (xElement != null)
                    {
                        fieldValue = xElement.Value;
                        return true;
                    }
                }
            }
            catch (Exception exception)
            {
                return false;
            }
            return false;
        }

	    protected IEnumerable ExecuteXPathQueryOnXElement(XElement xElement, string query, ref LevelLogger logger)
	    {
	        var executeXPathQueryLogger = logger.CreateLevelLogger();
            if (xElement != null)
            {
                try
                {
                    if (query == Dot)
                    {
                        return GetInnerXml(xElement);
                    }
                    return xElement.XPathEvaluate(query) as IEnumerable;
                }
                catch (Exception ex)
                {
                    executeXPathQueryLogger.AddError("Exception occured ExecuteXPathQueryOnXElement executing the XPath", String.Format("An exception occured in the ExecuteXPathQueryOnXElement method executing the XPath query. Query: {0}. Exception: {1}.", query, GetExceptionDebugInfo(ex)));
                }
            }
            executeXPathQueryLogger.AddError("XDocument was null in ExecuteXPathQueryOnXElement", "In ExecuteXPathQueryOnXElement method the XDocument was null.");
            return null;
        }

	    private static IEnumerable GetInnerXml(XElement xElement)
	    {
	        var innerXml = xElement.GetInnerXml();
	        return innerXml;
	    }

	    protected IList<object> ExecuteXPathQuery(XDocument xDocument)
        {
            if (xDocument != null)
            {
                var elements = xDocument.XPathSelectElements(Query);
                IList<object> list = new List<object>();
                foreach (var element in elements)
                {
                    list.Add(element);
                }
                return list;
            }
            Logger.AddError("Error", "In ExecuteXPathQuery method the XDocument was null.");
            return null;
        }

        public override string GetImportRowDebugInfo(object importRow)
        {
            if (importRow != null)
            {
                var getValueFromFieldLogger = Logger.CreateLevelLogger();
                var keyValue = GetValueFromFieldToIdentifyTheSameItemsBy(importRow, ref getValueFromFieldLogger);
                if (getValueFromFieldLogger.HasErrors())
                {
                    getValueFromFieldLogger.AddError("Error", String.Format("In the GetImportRowDebugInfo method failed."));
                    return keyValue;
                }

                if (!string.IsNullOrEmpty(keyValue))
                {
                    return keyValue;
                }
                if (importRow is XElement)
                {
                    var xElement = (XElement)importRow;
                    var innerXml = xElement.GetInnerXml();
                    if (!string.IsNullOrEmpty(innerXml))
                    {
                        if (innerXml.Length > DebugImportRowXmlCharacterLength)
                        {
                            return innerXml.Substring(0, DebugImportRowXmlCharacterLength);
                        }
                        return innerXml;
                    }
                }
                return importRow.ToString();
            }
            return String.Empty;
        }

	    #endregion Override Methods

        #region Methods

        protected string XMLFileData
        {
            get
            {
                var datasource = DataSourceString;
                var xmlData = GetXmlDataFromUrl(datasource);
                if (xmlData != null)
                {
                    return xmlData;
                }

                if (File.Exists(datasource))
                {
                    StreamReader streamreader = null;
                    try
                    {
                        streamreader = new StreamReader(datasource, true);
                        var fileStream = streamreader.ReadToEnd();
                        return fileStream;
                    }
                    catch (Exception ex)
                    {
                        Logger.AddError("Error", String.Format("Reading the file failed with an exception. Exception: {0}.", ex));
                        if (streamreader != null)
                        {
                            streamreader.Close();
                        }
                    }
                    finally
                    {
                        if (streamreader != null)
                        {
                            streamreader.Close();
                        }
                    }
                }
                else
                {
                    Logger.AddError("Error",
                                   String.Format(
                                       "The DataSource filepath points to a file that doesnt exist. DataSource: '{0}'",
                                       DataSourceString));
                }
                return string.Empty;
            }
        }

	    protected virtual string GetXmlDataFromUrl(string datasource)
	    {
            if (DataSourceCache != null && DataSourceCache.ContainsKey(datasource))
            {
                var xmlString = DataSourceCache[datasource];
                if (String.IsNullOrEmpty(xmlString))
                {
                    Logger.AddError("Xml was null or empty in GetxmlDataFromUrl. Proceed without cache.",
                        String.Format("The Xml retrieved from cache was null or empty. datasource: {0}.", datasource));
                }
                return xmlString;
            }
	        if (!String.IsNullOrEmpty(datasource) && datasource.StartsWith(UrlPrefix))
	        {
	            var myUri = new Uri(datasource);
	            var myHttpWebRequest = (HttpWebRequest) WebRequest.Create(myUri);

	            try
	            {
	                var myHttpWebResponse = (HttpWebResponse) myHttpWebRequest.GetResponse();
	                using (var streamResponse = myHttpWebResponse.GetResponseStream())
	                {
	                    if (streamResponse != null)
	                    {
	                        using (var xmlResponse = XmlReader.Create(streamResponse))
	                        {
	                            var xDoc = XDocument.Load(xmlResponse);
	                            var xmlString = xDoc.ToString();
                                if (DataSourceCache != null)
                                {
                                    if (String.IsNullOrEmpty(xmlString))
                                    {
                                        Logger.AddInfo("Xml was null or empty",
                                            String.Format(
                                                "Xml retrieved in GetXmlDataFromUrl was null or empty. Datasource: {0}.",
                                                datasource));
                                    }
                                    else
                                    {
                                        if (DataSourceCache.ContainsKey(datasource))
                                        {
                                            DataSourceCache[datasource] = xmlString;
                                        }
                                        else
                                        {
                                            DataSourceCache.Add(datasource, xmlString);
                                        }
                                    }
                                }
	                            return xmlString;
	                        }
	                    }
	                }
	            }
	            catch (Exception ex)
	            {
	                Logger.AddError("Error",
	                               String.Format("Reading the Url in XmlFileData failed with an exception. Exception: {0}.", ex));
	            }
	            Logger.AddError("Error",
	                           String.Format(
	                               "The URL provided failed loading any xml data. DataSource: '{0}'",
	                               DataSourceString));
	            return String.Empty;
	        }
            return String.Empty;
	    }

	    #endregion Methods
    }
}
