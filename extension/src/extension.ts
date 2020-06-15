import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import * as vscode from 'vscode';
import * as JSON5 from 'json5';

export function activate(context: vscode.ExtensionContext) {

    console.log('Activating "fastcodenav" extension');

    let disposable = vscode.commands.registerCommand('FastCodeNav.enablePlugin', async () => {
        if (os.platform() !== 'win32') {
            await vscode.window.showInformationMessage('FastCodeNav extension at the moment works only on Windows OS');
            return;
        }

        if (!process.env["USERPROFILE"]) {
            await vscode.window.showErrorMessage('Could not enabled FastCodeNav plugin because environment variable USERPROFILE is not defined');
            return;
        }

        const pluginAssemblyName = "FastCodeNavPlugin.dll";
        const pluginFlavor: string = process.env["__DEBUG_FASTCODENAV_PLUGIN"] ? "Debug" : "Release";
        const pluginAssemblyPath: string = path.join(__dirname, pluginFlavor, pluginAssemblyName);
        if (!fs.existsSync(pluginAssemblyPath)) {
            await vscode.window.showErrorMessage(`FastCodeNav plugin assembly ${pluginAssemblyPath} was not found`);
            return;
        }

        const omnisharpJsonPath = path.join(process.env["USERPROFILE"], ".omnisharp", "omnisharp.json");
        let omnisharpOptions: any = null;
        if (fs.existsSync(omnisharpJsonPath)) {
            omnisharpOptions = JSON5.parse(fs.readFileSync(omnisharpJsonPath).toString());
        }

        const updatedPluginLocations: string[] = [];
        let alreadyEnabled: boolean = false;
        if (omnisharpOptions && omnisharpOptions["Plugins"] && omnisharpOptions["Plugins"]["LocationPaths"]) {
            let plugins: string[] = omnisharpOptions["Plugins"]["LocationPaths"];
            plugins.forEach(pluginPath => {
                if (pluginPath === pluginAssemblyPath) {
                    alreadyEnabled = true;
                } else if (!pluginPath.endsWith(pluginAssemblyName)) {
                    updatedPluginLocations.push(pluginPath);
                }
            });
        }

        if (alreadyEnabled) {
            await vscode.window.showInformationMessage(`FastCodeNav plugin is already configured in ${omnisharpJsonPath}`);
            return;
        }

        updatedPluginLocations.push(pluginAssemblyPath);
        if (!omnisharpOptions) {
            omnisharpOptions = {};
        }

        if (!omnisharpOptions["Plugins"]) {
            omnisharpOptions["Plugins"] = {};
        }

        omnisharpOptions["Plugins"]["LocationPaths"] = updatedPluginLocations;

        let omnisharpJsonBakPath: string = "";
        if (fs.existsSync(omnisharpJsonPath)) {
            let i: number = 1;
            do {
                omnisharpJsonBakPath = path.join(process.env["USERPROFILE"], ".omnisharp", `omnisharp${i}.json`);
                i++;
            } while (fs.existsSync(omnisharpJsonBakPath));
    
            fs.copyFileSync(omnisharpJsonPath, omnisharpJsonBakPath);
        }

        fs.writeFileSync(omnisharpJsonPath, JSON.stringify(omnisharpOptions, null, 4));

        let restartOmniSharpMessage: string;
        if (omnisharpJsonBakPath) {
            restartOmniSharpMessage = `OmniSharp options in ${omnisharpJsonPath} has changed (previous were backed up as ${omnisharpJsonBakPath}). ` +
            `Would you like to relaunch the OmniSharp server with these changes?`;
        } else {
            restartOmniSharpMessage = `OmniSharp options were saved to ${omnisharpJsonPath}. ` +
            `Would you like to relaunch the OmniSharp server with these changes?`;
        }

        try {
            const value = await vscode.window.showInformationMessage(restartOmniSharpMessage, { title: "Restart OmniSharp" });
            if (value) {
                await vscode.commands.executeCommand('o.restart');
            }
        }
        catch (err) {
            console.log(err);
        }
    });

    context.subscriptions.push(disposable);
}

export function deactivate() { }
