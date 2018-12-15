package com.horine.emailAttachmentDownloader;

import com.sun.mail.util.BASE64DecoderStream;
import com.sun.mail.util.BASE64EncoderStream;

import java.io.*;

public class GlobalSettings {
    private String cfgFilepath;

    private String saveFolder;
    private String popHost;
    private String storeType;
    private String account;
    private String password;
    private String[] keywords;



    public GlobalSettings(String cfgFilepath){
        this.cfgFilepath = cfgFilepath;
    }

    public void getData() {
        String datain = "";
        File infile = new File(cfgFilepath);
        try {
            FileInputStream fis = new FileInputStream(infile);
            BASE64DecoderStream decodeStream = new BASE64DecoderStream(fis);
            InputStreamReader isr = new InputStreamReader(decodeStream, "UTF-8");
            BufferedReader in = new BufferedReader(isr);
            String line;
            while ((line = in.readLine()) != null){
                datain += line;
            }
            in.close();

        } catch (FileNotFoundException e) {
            e.printStackTrace();
        } catch (IOException e) {
            e.printStackTrace();
        }

        System.out.println(datain);
        String[] splitvals = datain.split(";");
        saveFolder = splitvals[0];
        account = splitvals[1];
        password = splitvals[2];
        popHost = splitvals[3];
        storeType = splitvals[4];
        setKeywords(splitvals[5]);
    }

    public void saveData(){
        String dataout = "";
        dataout += saveFolder + ";"
                + account + ";"
                + password + ";"
                + popHost + ";"
                + storeType + ";"
                + getKeywordString() + ";";
        File outFile = new File(cfgFilepath);
        try {
            FileOutputStream fos = new FileOutputStream(outFile);
            BASE64EncoderStream encodeStream = new BASE64EncoderStream(fos);
            OutputStreamWriter osw = new OutputStreamWriter(encodeStream, "UTF-8");
            BufferedWriter out = new BufferedWriter(osw);
            out.write(dataout);
            out.close();
        } catch (IOException e) {
            e.printStackTrace();
        }

    }

    public String getCfgFilepath() {
        return cfgFilepath;
    }

    public void setCfgFilepath(String cfgFilepath) {
        this.cfgFilepath = cfgFilepath;
    }

    public String getSaveFolder() {
        return saveFolder;
    }

    public void setSaveFolder(String saveFolder) {
        this.saveFolder = saveFolder;
    }

    public String getPopHost() {
        return popHost;
    }

    public void setPopHost(String popHost) {
        this.popHost = popHost;
    }

    public String getStoreType() {
        return storeType;
    }

    public void setStoreType(String storeType) {
        this.storeType = storeType;
    }

    public String getAccount() {
        return account;
    }

    public void setAccount(String account) {
        this.account = account;
    }

    public String getPassword() {
        return password;
    }

    public void setPassword(String password) {
        this.password = password;
    }

    public String getKeywordString() {
        String keywordString = "";
        keywordString += keywords[0];
        for (int i = 1; i < keywords.length; i++){
            keywordString += "," + keywords[i];
        }
        return keywordString;
    }
    public String[] getKeywords() {
        return keywords;
    }

    public void setKeywords(String keywords) {
        String[] keywordArray = keywords.split(",");
        this.keywords = keywordArray;
    }
    public void setKeywords(String[] keywords) {
        this.keywords = keywords;
    }

    public void splitKeywordString(String keywords){
        String splitString = "";

        String[] keywordArray = keywords.split(",");
        for (int i = 0; i < keywordArray.length; i++){
            if (keywordArray[i].length() == 0 || keywordArray[i].length() == 1 && keywordArray[i] == " "){
                continue;
            }
            else{
                String[] keywordspaceArray = keywordArray[i].split(" ");
                for (int j = 0; j < keywordspaceArray.length; j++) {
                    if (keywordspaceArray[j].length() == 0 || keywordspaceArray[j].length() == 1 && keywordspaceArray[j] == " "){
                        continue;
                    }
                    else if (!splitString.equals("")){
                        splitString += "," + keywordspaceArray[j];
                    }
                    else {
                        splitString += keywordspaceArray[j];
                    }
                }
            }
        }
        this.setKeywords(splitString);
    }
}
