//------------------------------------------------------------------------------
// <copyright file="SmtpSection.cs" company="Microsoft Corporation">
//     
//      Copyright (c) 2006 Microsoft Corporation.  All rights reserved.
//     
//      The use and distribution terms for this software are contained in the file
//      named license.txt, which can be found in the root of this distribution.
//      By using this software in any fashion, you are agreeing to be bound by the
//      terms of this license.
//     
//      You must not remove this notice, or any other, from this software.
//     
// </copyright>
//------------------------------------------------------------------------------

namespace System.Net.Configuration
{
    using System;
    using System.Configuration;
    using System.ComponentModel;
    using System.Globalization;
    using System.Net;
    using System.Net.Mail;
    using System.Reflection;
    using System.Threading;

    public sealed class SmtpSection : ConfigurationSection
    {
        public SmtpSection()
        {
            this.properties.Add(this.deliveryMethod);
            this.properties.Add(this.from);

            this.properties.Add(this.network);
            this.properties.Add(this.specifiedPickupDirectory);
        }

        [ConfigurationProperty(ConfigurationStrings.DeliveryMethod, DefaultValue = (SmtpDeliveryMethod) SmtpDeliveryMethod.Network)]
        public SmtpDeliveryMethod DeliveryMethod
        {
            get { 
                return (SmtpDeliveryMethod)this[this.deliveryMethod]; 
            }
            set { 
                this[this.deliveryMethod] = value; 
            }
        }

        [ConfigurationProperty(ConfigurationStrings.From)]
        public string From
        {
            get { return (string)this[this.from]; }
            set { this[this.from] = value; }
        }


        [ConfigurationProperty(ConfigurationStrings.Network)]
        public SmtpNetworkElement Network
        {
            get { 
                return (SmtpNetworkElement)this[this.network]; 
            }
        }

        [ConfigurationProperty(ConfigurationStrings.SpecifiedPickupDirectory)]
        public SmtpSpecifiedPickupDirectoryElement SpecifiedPickupDirectory
        {
            get { 
                return (SmtpSpecifiedPickupDirectoryElement)this[this.specifiedPickupDirectory]; 
            }
        }

        protected override ConfigurationPropertyCollection Properties 
        {
            get 
            {
                return this.properties;
            }
        }

	        

        ConfigurationPropertyCollection properties = new ConfigurationPropertyCollection();

        readonly ConfigurationProperty from =
            new ConfigurationProperty(ConfigurationStrings.From, typeof(string), null,
                    ConfigurationPropertyOptions.None);

        readonly ConfigurationProperty network =
            new ConfigurationProperty(ConfigurationStrings.Network, typeof(SmtpNetworkElement), null,
                    ConfigurationPropertyOptions.None);

        readonly ConfigurationProperty specifiedPickupDirectory =
            new ConfigurationProperty(ConfigurationStrings.SpecifiedPickupDirectory, typeof(SmtpSpecifiedPickupDirectoryElement), null,
                    ConfigurationPropertyOptions.None);

        readonly ConfigurationProperty deliveryMethod =
            new ConfigurationProperty(ConfigurationStrings.DeliveryMethod, typeof(SmtpDeliveryMethod), SmtpDeliveryMethod.Network, new SmtpDeliveryMethodTypeConverter(), null,
                    ConfigurationPropertyOptions.None);

        class SmtpDeliveryMethodTypeConverter : TypeConverter {
            public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType) {
                if (sourceType == typeof(string)) {
                    return true;
                }
                return base.CanConvertFrom(context, sourceType);
            }
    
            public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value) {
                string s = value as string;
                if (s != null) {
                    s = s.ToLower(CultureInfo.InvariantCulture);
                    switch (s) {
                        case "network":
                            return SmtpDeliveryMethod.Network;
                        case "specifiedpickupdirectory":
                            return SmtpDeliveryMethod.SpecifiedPickupDirectory;
                    }
                }
    
                return base.ConvertFrom(context, culture, value);
            }
        }
    }

    internal sealed class SmtpSectionInternal
    {
        internal SmtpSectionInternal(SmtpSection section)
        {
            this.deliveryMethod = section.DeliveryMethod;
            this.from = section.From;

            this.network = new SmtpNetworkElementInternal(section.Network);
            this.specifiedPickupDirectory = new SmtpSpecifiedPickupDirectoryElementInternal(section.SpecifiedPickupDirectory);
        }

        internal SmtpDeliveryMethod DeliveryMethod
        {
            get { return this.deliveryMethod; }
        }

        internal SmtpNetworkElementInternal Network
        {
            get { return this.network; }
        }

        internal string From
        {
            get { return this.from; }
        }

        internal SmtpSpecifiedPickupDirectoryElementInternal SpecifiedPickupDirectory
        {
            get { return this.specifiedPickupDirectory; }
        }

        SmtpDeliveryMethod                          deliveryMethod;
        string                                      from = null;
        SmtpNetworkElementInternal                  network = null;
        SmtpSpecifiedPickupDirectoryElementInternal specifiedPickupDirectory = null;

        internal static object ClassSyncObject
        {
            get
            {
                if (classSyncObject == null)
                {
                    Interlocked.CompareExchange(ref classSyncObject, new object(), null);
                }
                return classSyncObject;
            }
        }

        internal static SmtpSectionInternal GetSection()
        {
            lock (ClassSyncObject)
            {
                SmtpSection section = PrivilegedConfigurationManager.GetSection(ConfigurationStrings.SmtpSectionPath) as SmtpSection;
                if (section == null)
                    return null;

                return new SmtpSectionInternal(section);
            }
        }

        private static object classSyncObject;
    }
}


