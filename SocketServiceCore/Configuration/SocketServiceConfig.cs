using System;
using System.Collections.Generic;
using System.Text;
using System.Configuration;
using SuperSocket.SocketServiceCore.Config;

namespace SuperSocket.SocketServiceCore.Configuration
{
	public class SocketServiceConfig : ConfigurationSection, IConfig
	{

		[ConfigurationProperty("servers")]
		public ServerCollection Servers
		{
			get
			{
				return this["servers"] as ServerCollection;
			}
		}

		[ConfigurationProperty("services")]
		public ServiceCollection Services
		{
			get
			{
				return this["services"] as ServiceCollection;
			}
		}

		[ConfigurationProperty("certificate")]
		public CertificateConfig Certificate
		{
			get
			{
				return this["certificate"] as CertificateConfig;
			}
		}

		[ConfigurationProperty("credential")]
		public CredentialConfig Credential
		{
			get
			{
				return this["credential"] as CredentialConfig;
			}
		}

		#region IConfig Members

		public List<IServerConfig> GetServerList()
		{
			List<IServerConfig> serverList = new List<IServerConfig>();
			
			foreach(Server server in Servers)
			{
				serverList.Add(server);
			}
			
			return serverList;
		}

		public ICertificateConfig GetCertificate()
		{
			return Certificate;
		}

		public List<IServiceConfig> GetServiceList()
		{
			List<IServiceConfig> serviceList = new List<IServiceConfig>();

			foreach (Service service in Services)
			{
				serviceList.Add(service);
			}

			return serviceList;
		}

		public ICredentialConfig CredentialConfig
		{
			get { return Credential; }
		}

		#endregion
	}
}