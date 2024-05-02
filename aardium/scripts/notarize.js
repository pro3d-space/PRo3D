require('dotenv').config();
const { notarize } = require('@electron/notarize');


exports.default = async function(context) {

	const { electronPlatformName, appOutDir } = context;
	if (electronPlatformName !== 'darwin') {
		return;
	}
 
    const appName = context.packager.appInfo.productFilename;
 
	notarize({
	  appBundleId: 'space.pro3d.app',
	  appPath: `${appOutDir}/${appName}.app`,
	  appleId: "gh@aardworx.at",
	  appleIdPassword: process.env.MAC_DEV_PASSWORD,
	  teamId: "4LQPQ4H9LQ"
	}).catch(e => {
	  console.error("Didn't work :( " + e.message) // eslint-disable-line no-console
	});
}   