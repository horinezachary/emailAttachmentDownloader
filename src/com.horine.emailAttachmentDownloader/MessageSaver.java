package com.horine.emailAttachmentDownloader;

import com.sun.mail.util.BASE64DecoderStream;
import com.sun.mail.util.BASE64EncoderStream;

import java.io.*;
import java.util.ArrayList;

public class MessageSaver {

    final String FILEPATH = ".messages";

    public MessageSaver(){
    }

    public void saveElements(ArrayList<DisplayElem> elements){
        String dataout = "";

        for (int i = 0; i < elements.size(); i++){
            DisplayElem elem = elements.get(i);
            String elemString = "";
            elemString += elem.getId() + ";";
            elemString += elem.getDate() + ";";
            elemString += elem.getText() + ";";
            elemString += "\n";
            dataout += elemString;
        }

        File outFile = new File(FILEPATH);
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

    public ArrayList<DisplayElem> loadElements(DisplayPage displayPage){
        ArrayList<DisplayElem> elements = new ArrayList<DisplayElem>();

        ArrayList<String> datain = new ArrayList<String>();
        File infile = new File(FILEPATH);
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
