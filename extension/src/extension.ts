import * as vscode from 'vscode';

export function activate(context: vscode.ExtensionContext) {

    console.log('Activating "fastcodenav" extension');

    let disposable = vscode.commands.registerCommand('extension.enableFastCodeNavPlugin', () => {
        vscode.window.showInformationMessage('Hello World!');
    });

    context.subscriptions.push(disposable);
}

export function deactivate() { }
