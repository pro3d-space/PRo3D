require('dotenv').config();
const { notarize } = require('@electron-notarize');


exports.default = async function(context) {

	const { electronPlatformName, appOutDir } = context;
	if (electronPlatformName !== 'darwin') {
		return;
	}
 
	notarize({
	  appBundleId: 'space.pro3d.app',
	  appPath: `${appOutDir}/${appName}.app`,
	  appleId: "gh@aardworx.at",
	  appleIdPassword: process.env.MAC_DEV_PASSWORD,
	  ascProvider: '4LQPQ4H9LQ',
	}).catch(e => {
	  console.error("Didn't work :( " + e.message) // eslint-disable-line no-console
	});
}   