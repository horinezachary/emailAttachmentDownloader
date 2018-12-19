package com.horine.emailAttachmentDownloader;

import com.sun.mail.util.BASE64DecoderStream;

import java.io.*;
import java.util.ArrayList;

public class MessageSaver {

    String filepath;

    public MessageSaver(String filepath){
        this.filepath = filepath;
    }

    public void saveElements(ArrayList<DisplayElem> elements){
        //TODO message file saving
    }

    public ArrayList<DisplayElem> loadElements(DisplayPage displayPage){
        ArrayList<DisplayElem> elements = new ArrayList<DisplayElem>();

        ArrayList<String> datain = new ArrayList<String>();
        File infile = new File(filepath);
        try {
            FileInputStream fis = new FileInputStream(infile);
            BASE64DecoderStream decodeStream = new BASE64DecoderStream(fis);
            InputStreamReader isr = new InputStreamReader(decodeStream, "UTF-8");
            BufferedReader in = new BufferedReader(isr);
            String line;
            while ((line = in.readLine()) != null){
                datain.add(line);
            }
            in.close();
        } catch (FileNotFoundException e) {
            e.printStackTrace();
        } catch (IOException e) {
            e.printStackTrace();
        }

        for (int i = 0; i < datain.size(); i++) {
            String[] elementData = datain.get(i).split(";");
            DisplayElem elem = new DisplayElem(displayPage, Integer.parseInt(elementData[0]), elementData[1], elementData[2]);
            elements.add(elem);
        }
        return elements;
    }
}
